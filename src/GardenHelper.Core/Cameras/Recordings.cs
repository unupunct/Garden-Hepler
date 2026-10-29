using GardenHelper.Core.Data;
using GardenHelper.Core.Services;

namespace GardenHelper.Core.Cameras;

/// <summary>Something a camera noticed or was asked to keep: motion in the picture, a signal from a motion sensor, a timed snapshot or a manual one.</summary>
public class CameraEvent
{
    public int Id { get; set; }
    public int CameraId { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>motion, sensor, snapshot or manual.</summary>
    public string Kind { get; set; } = "motion";
    /// <summary>For picture motion: percentage of the picture that changed.</summary>
    public double? Score { get; set; }
    /// <summary>Saved picture, relative to the recordings folder.</summary>
    public string? File { get; set; }
    public string? Note { get; set; }
}

/// <summary>Compares two small grey-scale pictures (64 x 48) to tell whether something moved.</summary>
public static class MotionDetector
{
    public const int Width = 64, Height = 48;

    /// <summary>Percentage (0-100) of pixels whose brightness changed by more than <paramref name="pixelDelta"/>. 0 when the sizes differ.</summary>
    public static double ChangedPercent(byte[] a, byte[] b, int pixelDelta = 24)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        var changed = 0;
        for (var i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > pixelDelta) changed++;
        return 100.0 * changed / a.Length;
    }

    /// <summary>Sensitivity 1-100 to the changed share that counts as motion: 100 reacts to about 0.5% of the picture, 1 needs about 10%.</summary>
    public static double ThresholdPercent(int sensitivity) => Math.Clamp(10 - Math.Clamp(sensitivity, 1, 100) * 0.095, 0.5, 10);

    /// <summary>Motion if enough changed, but not when nearly everything changed at once (the camera switching to night mode, auto-exposure, a passing cloud).</summary>
    public static bool IsMotion(byte[] previous, byte[] current, int sensitivity, out double percent)
    {
        percent = ChangedPercent(previous, current);
        return percent >= ThresholdPercent(sensitivity) && percent < 65;
    }
}

/// <summary>Saved camera pictures on disk: where they go and how old ones are removed.</summary>
public sealed class RecordingStore
{
    private readonly AppPaths _paths;
    public RecordingStore(AppPaths paths) => _paths = paths;

    public string Root => _paths.RecordingsDir;

    /// <summary>Writes a picture as camera/date/time.jpg and returns the path relative to <see cref="Root"/>.</summary>
    public async Task<string> SaveAsync(int cameraId, byte[] picture, DateTime atLocal, CancellationToken ct = default)
    {
        var ext = picture.Length > 3 && picture[0] == 0x89 ? ".png" : ".jpg";
        var rel = Path.Combine(cameraId.ToString(), atLocal.ToString("yyyy-MM-dd"), atLocal.ToString("HHmmss-fff") + ext);
        var full = Path.Combine(Root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, picture, ct);
        return rel;
    }

    /// <summary>Full path of a saved picture, or null when the name is empty or tries to leave the recordings folder.</summary>
    public string? PathOf(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        var root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    public void Delete(string? relative)
    {
        if (PathOf(relative) is { } p && File.Exists(p)) { try { File.Delete(p); } catch (IOException) { /* in use: stays until the next clean-up */ } }
    }

    public long TotalBytes() => Directory.Exists(Root) ? new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
}

/// <summary>Records and lists camera events, and keeps the recordings from growing without limit.</summary>
public sealed class CameraEvents
{
    private readonly Repository _repo;
    private readonly RecordingStore _store;
    public CameraEvents(Repository repo, RecordingStore store) { _repo = repo; _store = store; }

    public Task<CameraEvent> AddAsync(int cameraId, string kind, double? score, string? file, string? note = null, DateTime? atUtc = null, CancellationToken ct = default) =>
        _repo.SaveAsync(new CameraEvent { CameraId = cameraId, Kind = kind, Score = score, File = file, Note = note, AtUtc = atUtc ?? DateTime.UtcNow }, ct);

    public Task<List<CameraEvent>> ListAsync(int cameraId, int take = 100, CancellationToken ct = default) =>
        _repo.ListAsync<CameraEvent>(q => q.Where(e => e.CameraId == cameraId).OrderByDescending(e => e.AtUtc).Take(take), ct);

    public async Task DeleteAsync(CameraEvent e, CancellationToken ct = default) { _store.Delete(e.File); await _repo.DeleteAsync(e, ct); }

    /// <summary>Removes events (and their pictures) older than <paramref name="keepDays"/> days. Returns how many were removed.</summary>
    public async Task<int> PurgeAsync(int cameraId, int keepDays, DateTime nowUtc, CancellationToken ct = default)
    {
        var cut = nowUtc.AddDays(-Math.Max(1, keepDays));
        var old = await _repo.ListAsync<CameraEvent>(q => q.Where(e => e.CameraId == cameraId && e.AtUtc < cut).Take(2000), ct);
        foreach (var e in old) await DeleteAsync(e, ct);
        return old.Count;
    }
}
