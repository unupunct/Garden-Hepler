using GardenHelper.Core.Data;

namespace GardenHelper.Core.Services;

/// <summary>Keeps pictures of seed packets in the app's data folder, so they survive when the original photo is moved or deleted.</summary>
public sealed class SeedPhotoStore
{
    public const long MaxBytes = 20L * 1024 * 1024;
    private readonly AppPaths _paths;
    public SeedPhotoStore(AppPaths paths) => _paths = paths;

    /// <summary>The extension for a picture format recognised by its first bytes (jpg, png, gif, bmp, webp), or null when it is not a picture.</summary>
    public static string? DetectExtension(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
        if (b.Length >= 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return ".gif";
        if (b.Length >= 2 && b[0] == 'B' && b[1] == 'M') return ".bmp";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ".webp";
        return null;
    }

    /// <summary>Copies a picture file into the store. Returns the stored file name, or null when the file is missing, too big or not a picture.</summary>
    public async Task<string?> AddFileAsync(string source, CancellationToken ct = default)
    {
        var info = new FileInfo(source);
        if (!info.Exists || info.Length is 0 or > MaxBytes) return null;
        return await AddBytesAsync(await File.ReadAllBytesAsync(source, ct), ct);
    }

    /// <summary>Stores picture bytes (for example from the clipboard). The file type comes from the content, never from the name.</summary>
    public async Task<string?> AddBytesAsync(byte[] bytes, CancellationToken ct = default)
    {
        if (bytes.Length is 0 or > (int)MaxBytes || DetectExtension(bytes) is not { } ext) return null;
        var name = Guid.NewGuid().ToString("N") + ext;
        await File.WriteAllBytesAsync(PathOf(name)!, bytes, ct);
        return name;
    }

    /// <summary>Full path of a stored picture, or null for an empty or unsafe name (only plain file names inside the store are accepted).</summary>
    public string? PathOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name)) return null;
        return Path.Combine(_paths.SeedPhotosDir, name);
    }

    public void Delete(string? name)
    {
        if (PathOf(name) is { } p && File.Exists(p)) { try { File.Delete(p); } catch (IOException) { /* in use: harmless, stays as an orphan */ } }
    }
}
