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

    private static readonly IReadOnlyDictionary<string, string> ExtensionToContentType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".mp4"] = "video/mp4",
            [".webm"] = "video/webm",
            [".mov"] = "video/quicktime",
        };

    public static string ContentTypeForExt(string ext)
    {
        foreach (var (contentType, meta) in Allowed)
            if (string.Equals(meta.Ext, ext, StringComparison.OrdinalIgnoreCase))
                return contentType;
        return "application/octet-stream";
    }

    public static bool TryGetMetaForExtension(string? extension, out string contentType, out (string Ext, string Kind) meta)
    {
        contentType = string.Empty;
        meta = default;
        if (string.IsNullOrWhiteSpace(extension)) return false;

        if (!ExtensionToContentType.TryGetValue(extension, out contentType))
            return false;

        return Allowed.TryGetValue(contentType, out meta);
    }

    public static bool TryDetectContentType(ReadOnlySpan<byte> header, out string contentType)
    {
        contentType = string.Empty;

        // JPEG: FF D8 FF
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            contentType = "image/jpeg";
            return true;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (header.Length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            contentType = "image/png";
            return true;
        }

        // GIF: GIF87a / GIF89a
        if (header.Length >= 6 &&
            header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F' &&
            header[3] == (byte)'8' && (header[4] == (byte)'7' || header[4] == (byte)'9') && header[5] == (byte)'a')
        {
            contentType = "image/gif";
            return true;
        }

        // WEBP: RIFF....WEBP
        if (header.Length >= 12 &&
            header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
            header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
        {
            contentType = "image/webp";
            return true;
        }

        // MP4/MOV (ISO BMFF): ....ftyp
        if (header.Length >= 12 &&
            header[4] == (byte)'f' && header[5] == (byte)'t' && header[6] == (byte)'y' && header[7] == (byte)'p')
        {
            var brand = System.Text.Encoding.ASCII.GetString(header.Slice(8, 4));
            if (brand.StartsWith("qt", StringComparison.OrdinalIgnoreCase))
                contentType = "video/quicktime";
            else
                contentType = "video/mp4";
            return true;
        }

        // WEBM/Matroska: 1A 45 DF A3
        if (header.Length >= 4 && header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
        {
            contentType = "video/webm";
            return true;
        }

        return false;
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
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (name.Contains("..", StringComparison.Ordinal)) return null;
        if (name.Contains('/') || name.Contains('\\')) return null;
        if (Path.IsPathRooted(name)) return null;
        if (Path.GetFileName(name) != name) return null;

        var fullPath = Path.GetFullPath(Path.Combine(Root, name));
        var fullRoot = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(fullRoot, StringComparison.Ordinal)) return null;

        return fullPath;
    }

    public bool Delete(string name)
    {
        var path = ResolvePath(name);
        if (path is null || !File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }
}
