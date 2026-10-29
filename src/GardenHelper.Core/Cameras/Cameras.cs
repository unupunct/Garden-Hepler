using System.Net.Http.Headers;
using System.Text;

namespace GardenHelper.Core.Cameras;

/// <summary>A network camera on the property. Position is stored as a real map coordinate so it stays right when the plan changes.</summary>
public class Camera
{
    public int Id { get; set; }
    public int GardenId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>snapshot (a URL that returns one JPEG), mjpeg (a motion-JPEG stream) or rtsp (not displayable yet).</summary>
    public string Kind { get; set; } = "snapshot";
    public string Url { get; set; } = "";
    public string? Username { get; set; }
    /// <summary>Password protected with the Windows account (DPAPI), base64. Never stored in plain text.</summary>
    public string? SecretProtected { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    /// <summary>Position on the garden plan in centimetres, used when the plan has no map position (and kept in step with it when it has).</summary>
    public double? PlanX { get; set; }
    public double? PlanY { get; set; }
    /// <summary>Direction the camera points, degrees clockwise from north.</summary>
    public double HeadingDeg { get; set; }
    public double FovDeg { get; set; } = 90;
    public double HeightCm { get; set; } = 250;
    /// <summary>The place (bed, coop, greenhouse…) the camera watches.</summary>
    public int? FeatureId { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Notes { get; set; }
    /// <summary>A <see cref="CameraPresets"/> key (hikvision, dahua, axis...): says how the picture address was built and whether the camera can be moved.</summary>
    public string? Brand { get; set; }
    /// <summary>Channel number on recorders and multi-lens cameras (1 for a single camera).</summary>
    public int Channel { get; set; } = 1;
    /// <summary>Watch the picture for movement and keep a picture when something moves.</summary>
    public bool MotionEnabled { get; set; }
    /// <summary>1-100: higher reacts to smaller movements.</summary>
    public int MotionSensitivity { get; set; } = 50;
    /// <summary>Keep one picture every this many minutes (a time-lapse); null = off.</summary>
    public int? SnapshotEveryMinutes { get; set; }
    /// <summary>Saved pictures older than this are deleted.</summary>
    public int KeepDays { get; set; } = 14;
    public override string ToString() => Name;
}

public enum CameraUrlProblem { None, Empty, NotAUrl, UnsupportedScheme }

public static class CameraUrl
{
    public static readonly string[] Kinds = { "snapshot", "mjpeg", "rtsp" };

    /// <summary>Only http and https are accepted. RTSP addresses are allowed as a saved setting but cannot be previewed yet.</summary>
    public static CameraUrlProblem Check(string kind, string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url)) return CameraUrlProblem.Empty;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return CameraUrlProblem.NotAUrl;
        var ok = kind == "rtsp" ? u.Scheme is "rtsp" or "rtsps" : u.Scheme is "http" or "https";
        if (!ok) return CameraUrlProblem.UnsupportedScheme;
        uri = u;
        return CameraUrlProblem.None;
    }
}

public class CameraException : Exception
{
    public CameraException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Fetches a picture from a camera over HTTP: a snapshot URL, or the first frame of an MJPEG stream.</summary>
public sealed class CameraClient
{
    private const int MaxBytes = 6 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly Func<System.Net.NetworkCredential, HttpClient> _digest;

    /// <param name="digestFactory">Makes a client that answers HTTP Digest logins (Hikvision, Dahua and Axis use them). The default one is fine; tests can replace it.</param>
    public CameraClient(HttpClient http, Func<System.Net.NetworkCredential, HttpClient>? digestFactory = null)
    {
        _http = http;
        _digest = digestFactory ?? (c => new HttpClient(new SocketsHttpHandler { Credentials = c, PreAuthenticate = true }) { Timeout = TimeSpan.FromSeconds(10) });
    }

    public async Task<byte[]> GrabFrameAsync(Camera camera, string? password, CancellationToken ct = default)
    {
        if (camera.Kind == "rtsp") throw new CameraException("RTSP cameras need a video player component, which is not built in yet. Use the camera's HTTP snapshot or MJPEG address.");
        if (CameraUrl.Check(camera.Kind, camera.Url, out var uri) != CameraUrlProblem.None || uri is null) throw new CameraException("The camera address is not a valid http or https URL.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var response = await SendWithLoginAsync(HttpMethod.Get, uri, null, camera.Username, password, cts.Token);
            if ((int)response.StatusCode is 401 or 403) throw new CameraException("The camera refused the login. Check the user name and password.");
            if (!response.IsSuccessStatusCode) throw new CameraException($"The camera answered with HTTP {(int)response.StatusCode}.");
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            return camera.Kind == "mjpeg" ? await ReadFirstFrameAsync(stream, cts.Token) : await ReadSingleImageAsync(stream, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new CameraException("The camera did not answer in time."); }
        catch (HttpRequestException ex) { throw new CameraException("The camera could not be reached: " + ex.Message, ex); }
    }

    /// <summary>Sends a request with the camera's login: Basic first, and when the camera asks for Digest, once more the Digest way.</summary>
    private async Task<HttpResponseMessage> SendWithLoginAsync(HttpMethod method, Uri uri, string? body, string? user, string? password, CancellationToken ct)
    {
        HttpRequestMessage Make(bool basic)
        {
            var r = new HttpRequestMessage(method, uri);
            if (body is not null) r.Content = new StringContent(body, Encoding.UTF8, "application/xml");
            if (basic && !string.IsNullOrEmpty(user)) r.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
            return r;
        }
        using var first = Make(true);
        var response = await _http.SendAsync(first, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(user) && response.Headers.WwwAuthenticate.Any(h => h.Scheme.Equals("Digest", StringComparison.OrdinalIgnoreCase)))
        {
            response.Dispose();
            var client = _digest(new System.Net.NetworkCredential(user, password ?? ""));
            using var second = Make(false);
            return await client.SendAsync(second, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        return response;
    }

    /// <summary>Moves or zooms a camera that supports it (see <see cref="CameraPtz"/>).</summary>
    public async Task MoveAsync(Camera camera, string direction, string? password, CancellationToken ct = default)
    {
        var req = CameraPtz.Build(camera, direction) ?? throw new CameraException("This camera cannot be moved from here. Choose its type (Hikvision, Dahua or Axis) in the camera settings.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            using var response = await SendWithLoginAsync(req.Method, req.Uri, req.Body, camera.Username, password, cts.Token);
            if ((int)response.StatusCode is 401 or 403) throw new CameraException("The camera refused the login.");
            if (!response.IsSuccessStatusCode) throw new CameraException($"The camera answered with HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new CameraException("The camera did not answer in time."); }
        catch (HttpRequestException ex) { throw new CameraException("The camera could not be reached: " + ex.Message, ex); }
    }

    private static async Task<byte[]> ReadSingleImageAsync(Stream s, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buf = new byte[16384]; int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0) { ms.Write(buf, 0, n); if (ms.Length > MaxBytes) throw new CameraException("The picture is larger than 6 MB."); }
        var data = ms.ToArray();
        if (!LooksLikeImage(data)) throw new CameraException("The address did not return a JPEG or PNG picture.");
        return data;
    }

    private static async Task<byte[]> ReadFirstFrameAsync(Stream s, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buf = new byte[16384]; int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ExtractJpeg(ms.GetBuffer().AsSpan(0, (int)ms.Length)) is { } frame) return frame;
            if (ms.Length > MaxBytes) break;
        }
        throw new CameraException("No picture was found in the video stream.");
    }

    public static bool LooksLikeImage(ReadOnlySpan<byte> d) =>
        d.Length > 4 && ((d[0] == 0xFF && d[1] == 0xD8) || (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47));

    /// <summary>Finds the first complete JPEG (start marker FFD8 to end marker FFD9) in a buffer of MJPEG data; null if not complete yet.</summary>
    public static byte[]? ExtractJpeg(ReadOnlySpan<byte> buffer)
    {
        var start = buffer.IndexOf(new byte[] { 0xFF, 0xD8 });
        if (start < 0) return null;
        var rest = buffer[(start + 2)..];
        var end = rest.IndexOf(new byte[] { 0xFF, 0xD9 });
        return end < 0 ? null : buffer.Slice(start, end + 4).ToArray();
    }
}
