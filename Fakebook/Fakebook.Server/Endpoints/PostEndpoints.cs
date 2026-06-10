using Fakebook.Server.Auth;
using Fakebook.Server.Data;
using Fakebook.Server.Domain;
using Fakebook.Server.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fakebook.Server.Endpoints;

public static class PostEndpoints
{
    public static RouteGroupBuilder MapPosts(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/posts").WithTags("Posts").RequireAuthorization();

        g.MapPost("/", async (CreatePostRequest req, HttpContext http, FakebookDbContext db) =>
        {
            var me = CurrentUser.Id(http.User);
            if (string.IsNullOrWhiteSpace(req.Content) && string.IsNullOrWhiteSpace(req.ImageUrl))
                return Results.BadRequest(new { error = "Post must have content or image" });

            var post = new Post
            {
                AuthorId  = me,
                Content   = req.Content ?? "",
                ImageUrl  = req.ImageUrl,
                MediaType = req.MediaType,
                Privacy   = req.Privacy
            };
            db.Posts.Add(post);
            db.Activities.Add(new Activity { UserId = me, Type = ActivityType.PostCreated, Summary = "Created a post", TargetPostId = post.Id });
            await db.SaveChangesAsync();

            return Results.Created($"/api/posts/{post.Id}", await LoadPostAsync(db, post.Id, me));
        });

        g.MapGet("/{postId:guid}", async (Guid postId, HttpContext http, FakebookDbContext db) =>
        {
            var me = CurrentUser.Id(http.User);
            var dto = await LoadPostAsync(db, postId, me);
            if (dto is null) return Results.NotFound();
            if (!await CanViewAsync(db, dto, me)) return Results.Forbid();
            return Results.Ok(dto);
        });

        g.MapGet("/user/{userId:guid}", async (Guid userId, HttpContext http, FakebookDbContext db, [FromQuery] int? skip, [FromQuery] int? take) =>
        {
            var me = CurrentUser.Id(http.User);
            var friends = await FriendEndpoints.GetFriendIdsAsync(db, me);
            var isMe = userId == me;
            var isFriend = friends.Contains(userId);

            var n = (take ?? 0) <= 0 ? 20 : Math.Min(take!.Value, 100);
            var s = Math.Max(skip ?? 0, 0);

            var q = db.Posts
                .AsNoTracking()
                .Where(p => p.AuthorId == userId && p.DeletedAt == null);

            if (!isMe)
            {
                q = q.Where(p => p.Privacy == PostPrivacy.Public ||
                                 (p.Privacy == PostPrivacy.FriendsOnly && isFriend));
            }

            var ids = await q.OrderByDescending(p => p.CreatedAt).Skip(s).Take(n).Select(p => p.Id).ToListAsync();
            return Results.Ok(await LoadPostsAsync(db, ids, me));
        });

        g.MapPut("/{postId:guid}", async (Guid postId, UpdatePostRequest req, HttpContext http, FakebookDbContext db) =>
        {
            var me = CurrentUser.Id(http.User);
            var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId && p.DeletedAt == null);
            if (post is null) return Results.NotFound();
            if (post.AuthorId != me) return Results.Forbid();

            post.Content   = req.Content;
            post.ImageUrl  = req.ImageUrl;
            post.MediaType = req.MediaType;
            post.Privacy   = req.Privacy;
            post.UpdatedAt = DateTime.UtcNow;
            db.Activities.Add(new Activity { UserId = me, Type = ActivityType.PostEdited, Summary = "Edited a post", TargetPostId = post.Id });
            await db.SaveChangesAsync();
            return Results.Ok(await LoadPostAsync(db, postId, me));
        });

        g.MapDelete("/{postId:guid}", async (Guid postId, HttpContext http, FakebookDbContext db) =>
        {
            var me = CurrentUser.Id(http.User);
            var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId && p.DeletedAt == null);
            if (post is null) return Results.NotFound();
            if (post.AuthorId != me) return Results.Forbid();

            post.DeletedAt = DateTime.UtcNow;
            db.Activities.Add(new Activity { UserId = me, Type = ActivityType.PostDeleted, Summary = "Deleted a post", TargetPostId = post.Id });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapPost("/{postId:guid}/share", async (Guid postId, SharePostRequest req, HttpContext http, FakebookDbContext db) =>
        {
            var me = CurrentUser.Id(http.User);
            var original = await db.Posts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postId && p.DeletedAt == null);
            if (original is null) return Results.NotFound();
            if (original.Privacy == PostPrivacy.Private && original.AuthorId != me)
                return Results.Forbid();

            // Persist both a Share row (for counting) and a shared Post row (so it shows in feeds).
            var sharedPost = new Post
            {
                AuthorId       = me,
                Content        = req.Message ?? "",
                Privacy        = PostPrivacy.Public,
                OriginalPostId = original.Id
            };
            db.Posts.Add(sharedPost);
            db.Shares.Add(new Share { UserId = me, PostId = original.Id, Message = req.Message });
            db.Activities.Add(new Activity { UserId = me, Type = ActivityType.Shared, Summary = "Shared a post", TargetPostId = original.Id });
            await db.SaveChangesAsync();

            return Results.Created($"/api/posts/{sharedPost.Id}", await LoadPostAsync(db, sharedPost.Id, me));
        });

        // ----- comments + reactions are nested under posts -----

        g.MapGet("/{postId:guid}/comments", async (Guid postId, FakebookDbContext db) =>
        {
            var comments = await db.Comments.AsNoTracking()
                .Where(c => c.PostId == postId && c.DeletedAt == null)
                .OrderBy(c => c.CreatedAt)
                .Include(c => c.Author)
                .Select(c => new CommentDto(
                    c.Id, c.PostId, c.ParentCommentId,
                    new UserSummary(c.Author!.Id, c.Author.Username, c.Author.DisplayName, c.Author.AvatarUrl),
                    c.Content, c.CreatedAt, c.UpdatedAt,
                    c.Reactions.Count))
                .ToListAsync();
            return Results.Ok(comments);
        });

        g.MapPost("/{postId:guid}/comments", async (Guid postId, CreateCommentRequest req, HttpContext http, FakebookDbContext db) =>
        {
            var me = CurrentUser.Id(http.User);
            var post = await db.Posts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postId && p.DeletedAt == null);
            if (post is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(req.Content)) return Results.BadRequest(new { error = "Content required" });

            var c = new Comment
            {
                PostId          = postId,
                AuthorId        = me,
                ParentCommentId = req.ParentCommentId,
                Content         = req.Content.Trim()
            };
            db.Comments.Add(c);
            db.Activities.Add(new Activity
            {
                UserId = me, Type = ActivityType.CommentCreated,
                Summary = "Commented on a post", TargetPostId = postId, TargetCommentId = c.Id
            });
            await db.SaveChangesAsync();
            var author = await db.Users.AsNoTracking().FirstAsync(u => u.Id == me);
            return Results.Created($"/api/posts/{postId}/comments/{c.Id}",
                new CommentDto(c.Id, c.PostId, c.ParentCommentId,
                    new UserSummary(author.Id, author.Username, author.DisplayName, author.AvatarUrl),
                    c.Content, c.CreatedAt, c.UpdatedAt, 0));
        });

        g.MapPut("/{postId:guid}/comments/{commentId:guid}", async (Guid postId, Guid commentId, UpdateCommentRequest req, HttpContext http, FakebookDbContext db) =>
        {
            var me = CurrentUser.Id(http.User);
            var c = await db.Comments.FirstOrDefaultAsync(x => x.Id == commentId && x.PostId == postId && x.DeletedAt == null);
            if (c is null) return Results.NotFound();
            if (c.AuthorId != me) return Results.Forbid();
            c.Content = req.Content;
            c.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapDelete("/{postId:guid}/comments/{commentId:guid}", async (Guid postId, Guid commentId, HttpContext http, FakebookDbContext db) =>
        {
            var me = CurrentUser.Id(http.User);
            var c = await db.Comments.FirstOrDefaultAsync(x => x.Id == commentId && x.PostId == postId && x.DeletedAt == null);
            if (c is null) return Results.NotFound();
            if (c.AuthorId != me) return Results.Forbid();
            c.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapPost("/{postId:guid}/reactions", async (Guid postId, ReactRequest req, HttpContext http, FakebookDbContext db) =>
        {
            var me = CurrentUser.Id(http.User);
            var post = await db.Posts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postId && p.DeletedAt == null);
            if (post is null) return Results.NotFound();

            var existing = await db.Reactions.FirstOrDefaultAsync(r => r.UserId == me && r.PostId == postId);
            if (existing is null)
            {
                db.Reactions.Add(new Reaction { UserId = me, PostId = postId, Type = req.Type });
                db.Activities.Add(new Activity { UserId = me, Type = ActivityType.Reacted, Summary = $"Reacted ({req.Type}) on a post", TargetPostId = postId });
            }
            else
            {
                existing.Type = req.Type;
            }
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapDelete("/{postId:guid}/reactions", async (Guid postId, HttpContext http, FakebookDbContext db) =>
        {
            var me = CurrentUser.Id(http.User);
            var r = await db.Reactions.FirstOrDefaultAsync(x => x.UserId == me && x.PostId == postId);
            if (r is null) return Results.NoContent();
            db.Reactions.Remove(r);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapPost("/{postId:guid}/comments/{commentId:guid}/reactions", async (Guid postId, Guid commentId, ReactRequest req, HttpContext http, FakebookDbContext db) =>
        {
            var me = CurrentUser.Id(http.User);
            var existing = await db.Reactions.FirstOrDefaultAsync(r => r.UserId == me && r.CommentId == commentId);
            if (existing is null)
                db.Reactions.Add(new Reaction { UserId = me, CommentId = commentId, Type = req.Type });
            else
                existing.Type = req.Type;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return api;
    }

    public static async Task<bool> CanViewAsync(FakebookDbContext db, PostDto dto, Guid viewerId)
    {
        if (dto.Author.Id == viewerId) return true;
        if (dto.Privacy == PostPrivacy.Public) return true;
        if (dto.Privacy == PostPrivacy.Private) return false;
        var friends = await FriendEndpoints.GetFriendIdsAsync(db, viewerId);
        return friends.Contains(dto.Author.Id);
    }

    public static async Task<PostDto?> LoadPostAsync(FakebookDbContext db, Guid postId, Guid viewerId)
    {
        var list = await LoadPostsAsync(db, new List<Guid> { postId }, viewerId);
        return list.FirstOrDefault();
    }

    public static async Task<List<PostDto>> LoadPostsAsync(FakebookDbContext db, IReadOnlyList<Guid> ids, Guid viewerId)
    {
        if (ids.Count == 0) return new List<PostDto>();

        // Pull posts + originals + counts + viewer reaction in batched queries.
        var posts = await db.Posts.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Include(p => p.Author)
            .ToListAsync();

        var originalIds = posts.Where(p => p.OriginalPostId != null).Select(p => p.OriginalPostId!.Value).Distinct().ToList();
        var originals = originalIds.Count == 0
            ? new List<Post>()
            : await db.Posts.AsNoTracking().Where(p => originalIds.Contains(p.Id)).Include(p => p.Author).ToListAsync();

        var allIds = ids.Concat(originalIds).Distinct().ToList();

        var commentCounts  = await db.Comments .Where(c => allIds.Contains(c.PostId) && c.DeletedAt == null).GroupBy(c => c.PostId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        var reactionCounts = await db.Reactions.Where(r => r.PostId != null && allIds.Contains(r.PostId!.Value)).GroupBy(r => r.PostId!.Value).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        var shareCounts    = await db.Shares   .Where(s => allIds.Contains(s.PostId)).GroupBy(s => s.PostId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        var myReactions    = await db.Reactions.Where(r => r.UserId == viewerId && r.PostId != null && allIds.Contains(r.PostId!.Value)).ToDictionaryAsync(r => r.PostId!.Value, r => r.Type);

        PostDto ToDto(Post p)
        {
            PostDto? orig = null;
            if (p.OriginalPostId is { } oid)
            {
                var op = originals.FirstOrDefault(x => x.Id == oid);
                if (op is not null) orig = MakeDto(op);
            }
            return MakeDto(p, orig);

            PostDto MakeDto(Post x, PostDto? original = null) => new(
                x.Id,
                new UserSummary(x.Author!.Id, x.Author.Username, x.Author.DisplayName, x.Author.AvatarUrl),
                x.Content,
                x.ImageUrl,
                x.MediaType,
                x.Privacy,
                x.CreatedAt,
                x.UpdatedAt,
                commentCounts.GetValueOrDefault(x.Id),
                reactionCounts.GetValueOrDefault(x.Id),
                shareCounts.GetValueOrDefault(x.Id),
                myReactions.TryGetValue(x.Id, out var t) ? t : (ReactionType?)null,
                original);
        }

        var byId = posts.ToDictionary(p => p.Id);
        return ids.Where(id => byId.ContainsKey(id)).Select(id => ToDto(byId[id])).ToList();
    }
}
