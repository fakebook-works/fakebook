namespace Fakebook.UploadServer;

public static class MediaEndpoints
{
    public static WebApplication MapMedia(this WebApplication app)
    {
        // Upload one image or video. Auth required (bearer token minted by the main API).
        app.MapPost("/media", async (HttpRequest req, FileStore store, CancellationToken ct) =>
        {
            if (!req.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data." });

            var form = await req.ReadFormAsync(ct);
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "No file uploaded." });

            if (!MediaTypes.Allowed.TryGetValue(file.ContentType, out var meta))
                return Results.BadRequest(new { error = $"Unsupported type '{file.ContentType}'. Allowed: jpg, png, gif, webp, mp4, webm, mov." });

            var limit = meta.Kind == "video" ? MediaTypes.MaxVideoBytes : MediaTypes.MaxImageBytes;
            if (file.Length > limit)
                return Results.BadRequest(new { error = $"File too large. Max {limit / 1024 / 1024} MB for {meta.Kind}s." });

            await using var stream = file.OpenReadStream();
            var name = await store.SaveAsync(stream, meta.Ext, ct);
            return Results.Ok(new MediaUploadResult($"/media/{name}", meta.Kind, file.ContentType, file.Length, name));
        })
        .RequireAuthorization()
        .DisableAntiforgery();

        // Serve a stored file. Public so <img>/<video> tags load without auth headers.
        // Range processing is enabled so videos can be seeked/streamed.
        app.MapGet("/media/{name}", (string name, FileStore store) =>
        {
            var path = store.ResolvePath(name);
            if (path is null || !File.Exists(path)) return Results.NotFound();
            var contentType = MediaTypes.ContentTypeForExt(Path.GetExtension(name));
            return Results.File(path, contentType, enableRangeProcessing: true);
        });

        // Remove a stored file. Auth required.
        app.MapDelete("/media/{name}", (string name, FileStore store) =>
            store.Delete(name) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization();

        return app;
    }
}
