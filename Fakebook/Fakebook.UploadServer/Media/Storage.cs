namespace Fakebook.UploadServer;

// Returned to the client after a successful upload. Url is same-origin and relative,
// e.g. "/media/ab12….jpg" — store it straight onto a Post / Listing / avatar.
public record MediaUploadResult(string Url, string Type, string ContentType, long Size, string Name);

// The content types we accept, with their canonical on-disk extension and media kind.
// The client-supplied filename extension is ignored; the extension here is derived from
// the validated content type so a stored name can never carry an unexpected extension.
public static class MediaTypes
{
    public const long MaxImageBytes = 10L * 1024 * 1024;   // 10 MB
    public const long MaxVideoBytes = 100L * 1024 * 1024;  // 100 MB

    public static readonly IReadOnlyDictionary<string, (string Ext, string Kind)> Allowed =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"]      = (".jpg",  "image"),
            ["image/png"]       = (".png",  "image"),
            ["image/gif"]       = (".gif",  "image"),
            ["image/webp"]      = (".webp", "image"),
            ["video/mp4"]       = (".mp4",  "video"),
            ["video/webm"]      = (".webm", "video"),
            ["video/quicktime"] = (".mov",  "video"),
        };

    public static string ContentTypeForExt(string ext)
    {
        foreach (var (contentType, meta) in Allowed)
            if (string.Equals(meta.Ext, ext, StringComparison.OrdinalIgnoreCase))
                return contentType;
        return "application/octet-stream";
    }
}

// Stores uploaded files on disk under a single root folder.
// Stored names are server-generated GUIDs, so reads only need path-traversal guards.
public sealed class FileStore(string root)
{
    // Hard ceiling for any single request body, above the per-kind limits.
    public const long MaxRequestBytes = 120L * 1024 * 1024;

    public string Root { get; } = root;

    public async Task<string> SaveAsync(Stream content, string ext, CancellationToken ct)
    {
        var name = Guid.NewGuid().ToString("N") + ext;
        await using var fs = File.Create(Path.Combine(Root, name));
        await content.CopyToAsync(fs, ct);
        return name;
    }

    // Resolve a request-supplied name to a real path, rejecting anything that
    // isn't a bare file name living directly under the root.
    public string? ResolvePath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..") || Path.GetFileName(name) != name)
            return null;
        return Path.Combine(Root, name);
    }

    public bool Delete(string name)
    {
        var path = ResolvePath(name);
        if (path is null || !File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }
}
