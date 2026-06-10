using Fakebook.Server.Domain;

namespace Fakebook.Server.Dtos;

// ----- Auth -----
public record RegisterRequest(string Username, string Email, string Password, string DisplayName);
public record LoginRequest(string UsernameOrEmail, string Password);
public record RefreshRequest(string RefreshToken);
public record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserSummary User);

// ----- User -----
public record UserSummary(Guid Id, string Username, string DisplayName, string? AvatarUrl);

public record UserProfile(
    Guid Id,
    string Username,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    string? Bio,
    DateOnly? BirthDate,
    string? Gender,
    string? Location,
    DateTime CreatedAt,
    int FriendCount,
    int PostCount);

public record UpdateProfileRequest(
    string? DisplayName,
    string? Bio,
    DateOnly? BirthDate,
    string? Gender,
    string? Location);

public record UpdateAvatarRequest(string AvatarUrl);

// ----- Friends -----
public record FriendRequestDto(Guid FriendshipId, UserSummary User, DateTime CreatedAt);
public record FriendDto(Guid FriendshipId, UserSummary User, DateTime Since);
public record SendFriendRequest(Guid TargetUserId);

// ----- Posts -----
public record CreatePostRequest(string Content, string? ImageUrl, string? MediaType, PostPrivacy Privacy);
public record UpdatePostRequest(string Content, string? ImageUrl, string? MediaType, PostPrivacy Privacy);
public record SharePostRequest(string? Message);

public record PostDto(
    Guid Id,
    UserSummary Author,
    string Content,
    string? ImageUrl,
    string? MediaType,
    PostPrivacy Privacy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int CommentCount,
    int ReactionCount,
    int ShareCount,
    ReactionType? MyReaction,
    PostDto? OriginalPost);

// ----- Comments -----
public record CreateCommentRequest(string Content, Guid? ParentCommentId);
public record UpdateCommentRequest(string Content);
public record CommentDto(
    Guid Id,
    Guid PostId,
    Guid? ParentCommentId,
    UserSummary Author,
    string Content,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int ReactionCount);

// ----- Reactions -----
public record ReactRequest(ReactionType Type);

// ----- Activity -----
public record ActivityDto(
    Guid Id,
    ActivityType Type,
    string? Summary,
    Guid? TargetPostId,
    Guid? TargetCommentId,
    Guid? TargetUserId,
    DateTime CreatedAt);

// ----- Marketplace -----
public record CreateListingRequest(
    string Title,
    string Description,
    string? ImageUrl,
    ListingCategory Category,
    string? Location,
    ListingType Type,
    decimal Price,
    int? AuctionDays);

public record PlaceBidRequest(decimal Amount);

public record BidDto(Guid Id, UserSummary Bidder, decimal Amount, DateTime CreatedAt);

public record ListingDto(
    Guid Id,
    UserSummary Seller,
    string Title,
    string? ImageUrl,
    ListingCategory Category,
    string? Location,
    ListingType Type,
    decimal Price,
    decimal CurrentPrice,
    int BidCount,
    DateTime? AuctionEndsAt,
    ListingStatus Status,
    DateTime CreatedAt);

public record ListingDetailDto(
    Guid Id,
    UserSummary Seller,
    string Title,
    string Description,
    string? ImageUrl,
    ListingCategory Category,
    string? Location,
    ListingType Type,
    decimal Price,
    decimal CurrentPrice,
    decimal MinNextBid,
    int BidCount,
    DateTime? AuctionEndsAt,
    ListingStatus Status,
    UserSummary? HighestBidder,
    UserSummary? Buyer,
    bool IsMine,
    DateTime CreatedAt,
    IReadOnlyList<BidDto> Bids);
