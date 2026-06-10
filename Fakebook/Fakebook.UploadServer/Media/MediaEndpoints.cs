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

            var ext = Path.GetExtension(file.FileName);
            if (!MediaTypes.TryGetMetaForExtension(ext, out _, out var extensionMeta))
                return Results.BadRequest(new { error = "Unsupported file extension. Allowed: jpg, png, gif, webp, mp4, webm, mov." });

            await using var stream = file.OpenReadStream();
            var header = new byte[32];
            var read = await stream.ReadAsync(header, 0, header.Length, ct);
            if (read <= 0)
                return Results.BadRequest(new { error = "Unable to read file header." });

            if (!MediaTypes.TryDetectContentType(header.AsSpan(0, read), out var detectedContentType) ||
                !MediaTypes.Allowed.TryGetValue(detectedContentType, out var magicMeta))
            {
                return Results.BadRequest(new { error = "Unsupported or invalid file signature." });
            }

            if (!string.Equals(extensionMeta.Ext, magicMeta.Ext, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "File extension does not match the file content." });
            }

            var limit = magicMeta.Kind == "video" ? MediaTypes.MaxVideoBytes : MediaTypes.MaxImageBytes;
            if (file.Length > limit)
                return Results.BadRequest(new { error = $"File too large. Max {limit / 1024 / 1024} MB for {magicMeta.Kind}s." });

            if (stream.CanSeek)
                stream.Position = 0;

            var name = await store.SaveAsync(stream, magicMeta.Ext, ct);
            return Results.Ok(new MediaUploadResult($"/media/{name}", magicMeta.Kind, detectedContentType, file.Length, name));
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
