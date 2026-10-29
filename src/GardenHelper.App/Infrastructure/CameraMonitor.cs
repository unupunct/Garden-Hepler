using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GardenHelper.Core.Cameras;
using GardenHelper.Core.Services;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.Infrastructure;

/// <summary>
/// Watches cameras in the background while the app is open: keeps a picture when something moves, and takes timed snapshots (a time-lapse).
/// Only cameras with motion watching or a snapshot interval are touched, so nothing happens until you switch it on for a camera.
/// </summary>
public sealed class CameraMonitor
{
    private sealed class State { public byte[]? Last; public DateTime LastEvent = DateTime.MinValue; public DateTime NextSnapshot = DateTime.MinValue; public DateTime NextTry = DateTime.MinValue; public int Failures; }

    private readonly Repository _repo;
    private readonly CameraClient _client;
    private readonly CameraEvents _events;
    private readonly RecordingStore _store;
    private readonly ILogger<CameraMonitor> _log;
    private readonly Dictionary<int, State> _state = new();
    private CancellationTokenSource? _cts;
    private DateTime _lastPurge = DateTime.MinValue;

    public CameraMonitor(Repository repo, CameraClient client, CameraEvents events, RecordingStore store, ILogger<CameraMonitor> log)
    { _repo = repo; _client = client; _events = events; _store = store; _log = log; }

    /// <summary>Raised on a background thread when a picture was kept.</summary>
    public event Action<CameraEvent>? EventRecorded;

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop() { _cts?.Cancel(); _cts = null; }

    private async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try { await TickAsync(ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning(ex, "Camera monitor tick failed"); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var cams = (await _repo.ListAsync<Camera>(q => q.Where(c => c.Enabled && c.Kind != "rtsp" && (c.MotionEnabled || c.SnapshotEveryMinutes != null)), ct));
        foreach (var gone in _state.Keys.Where(id => cams.All(c => c.Id != id)).ToList()) _state.Remove(gone);
        foreach (var cam in cams)
        {
            var st = _state.TryGetValue(cam.Id, out var s) ? s : _state[cam.Id] = new State();
            if (now < st.NextTry) continue;
            var wantSnapshot = cam.SnapshotEveryMinutes is > 0 && now >= st.NextSnapshot;
            if (!cam.MotionEnabled && !wantSnapshot) continue;
            try
            {
                var frame = await _client.GrabFrameAsync(cam, CameraSecrets.Unprotect(cam.SecretProtected), ct);
                st.Failures = 0;
                var gray = Gray(frame);
                if (cam.MotionEnabled && gray is not null && st.Last is not null && (now - st.LastEvent).TotalSeconds >= 15
                    && MotionDetector.IsMotion(st.Last, gray, cam.MotionSensitivity, out var pct))
                {
                    st.LastEvent = now;
                    await RecordAsync(cam, "motion", pct, frame, now, ct);
                }
                if (wantSnapshot) { st.NextSnapshot = now.AddMinutes(cam.SnapshotEveryMinutes!.Value); await RecordAsync(cam, "snapshot", null, frame, now, ct); }
                st.Last = gray;
            }
            catch (CameraException) { st.Failures++; st.NextTry = now.AddSeconds(Math.Min(120, 5 * st.Failures * st.Failures)); }   // back off while a camera is off or unreachable
        }
        if ((now - _lastPurge).TotalHours >= 1)
        {
            _lastPurge = now;
            foreach (var c in await _repo.ListAsync<Camera>(null, ct)) await _events.PurgeAsync(c.Id, c.KeepDays, now, ct);
        }
    }

    /// <summary>Saves a picture and writes the event. Used by the monitor, the Snapshot button and the motion-sensor API.</summary>
    public async Task<CameraEvent> RecordAsync(Camera cam, string kind, double? score, byte[] picture, DateTime utcNow, CancellationToken ct = default)
    {
        var rel = await _store.SaveAsync(cam.Id, picture, utcNow.ToLocalTime(), ct);
        var ev = await _events.AddAsync(cam.Id, kind, score, rel, null, utcNow, ct);
        EventRecorded?.Invoke(ev);
        return ev;
    }

    /// <summary>A 64 x 48 grey copy of a picture, small enough to compare quickly.</summary>
    public static byte[]? Gray(byte[] picture)
    {
        try
        {
            using var ms = new MemoryStream(picture);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.DecodePixelWidth = MotionDetector.Width; bmp.DecodePixelHeight = MotionDetector.Height; bmp.StreamSource = ms;
            bmp.EndInit(); bmp.Freeze();
            var conv = new FormatConvertedBitmap(bmp, PixelFormats.Gray8, null, 0);
            var buf = new byte[conv.PixelWidth * conv.PixelHeight];
            conv.CopyPixels(buf, conv.PixelWidth, 0);
            return buf.Length == MotionDetector.Width * MotionDetector.Height ? buf : null;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or IOException) { return null; }
    }
}
