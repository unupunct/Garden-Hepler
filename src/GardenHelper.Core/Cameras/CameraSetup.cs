using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace GardenHelper.Core.Cameras;

/// <summary>Known camera types: how to build the picture address from just an IP address.</summary>
public sealed record CameraPreset(string Key, string Kind, string PathTemplate, int Port, bool Ptz);

public static class CameraPresets
{
    public static readonly CameraPreset[] All =
    {
        new("generic", "snapshot", "/snapshot.jpg", 80, false),
        new("ipwebcam", "snapshot", "/shot.jpg", 8080, false),          // the "IP Webcam" Android app
        new("esp32", "snapshot", "/capture", 80, false),                 // ESP32-CAM example firmware
        new("mjpg-streamer", "snapshot", "/?action=snapshot", 8080, false), // Raspberry Pi with mjpg-streamer
        new("hikvision", "snapshot", "/ISAPI/Streaming/channels/{ch}01/picture", 80, true),
        new("dahua", "snapshot", "/cgi-bin/snapshot.cgi?channel={ch}", 80, true),   // Dahua, Amcrest and many OEM cameras
        new("axis", "snapshot", "/axis-cgi/jpg/image.cgi", 80, true),
        new("rtsp", "rtsp", "/stream1", 554, false),                      // saved, cannot be previewed yet
    };

    public static CameraPreset? Get(string? key) => All.FirstOrDefault(p => p.Key == key);

    /// <summary>The address for a camera at <paramref name="host"/> (an IP address or name, optionally with :port). Returns null for a host that is not usable.</summary>
    public static string? BuildUrl(CameraPreset preset, string? host, int? port = null, int channel = 1)
    {
        var h = (host ?? "").Trim();
        if (h.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) h = h[7..];
        else if (h.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) h = h[8..];
        else if (h.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)) h = h[7..];
        h = h.TrimEnd('/');
        if (h.Length == 0 || !Regex.IsMatch(h, @"^[A-Za-z0-9.\-]+(:\d{1,5})?$")) return null;
        var hasPort = h.Contains(':');
        var p = port ?? preset.Port;
        if (p is < 1 or > 65535) return null;
        var authority = hasPort ? h : (p == 80 && preset.Kind != "rtsp" ? h : $"{h}:{p}");
        var scheme = preset.Kind == "rtsp" ? "rtsp" : "http";
        return $"{scheme}://{authority}{preset.PathTemplate.Replace("{ch}", Math.Clamp(channel, 1, 64).ToString())}";
    }

    /// <summary>Guesses a preset from what a device says about itself (web server name or login realm).</summary>
    public static string? Guess(string? serverOrRealm)
    {
        var s = (serverOrRealm ?? "").ToLowerInvariant();
        if (s.Contains("hikvision") || s.Contains("dnvrs") || s.Contains("app-webs")) return "hikvision";
        if (s.Contains("dahua") || s.Contains("amcrest") || s.Contains("dh-")) return "dahua";
        if (s.Contains("axis")) return "axis";
        if (s.Contains("mjpg")) return "mjpg-streamer";
        if (s.Contains("ip webcam") || s.Contains("ipwebcam")) return "ipwebcam";
        if (s.Contains("esp32") || s.Contains("esp-")) return "esp32";
        return null;
    }
}

/// <summary>What a camera's QR code or pasted text contains.</summary>
public sealed record CameraQrResult(string Kind, string Url, string? Username, string? Password);

public static class CameraQrParser
{
    /// <summary>Reads an address such as http://user:pass@192.168.1.50:8080/shot.jpg or rtsp://user:pass@host/stream. Credentials are split off the address so they are stored encrypted, not in the URL. Anything else (a serial number, a Wi-Fi code) gives null.</summary>
    public static CameraQrResult? Parse(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0 || t.Length > 2000) return null;
        if (!t.Contains("://") && Regex.IsMatch(t, @"^[A-Za-z0-9.\-]+(:\d{1,5})?(/.*)?$") && t.Contains('.')) t = "http://" + t;    // "192.168.1.50:8080/shot.jpg"
        if (!Uri.TryCreate(t, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https" or "rtsp" or "rtsps")) return null;
        if (string.IsNullOrEmpty(u.Host)) return null;
        string? user = null, pass = null;
        if (!string.IsNullOrEmpty(u.UserInfo))
        {
            var parts = u.UserInfo.Split(':', 2);
            user = Uri.UnescapeDataString(parts[0]); pass = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null;
        }
        var clean = new UriBuilder(u) { UserName = "", Password = "" }.Uri.AbsoluteUri;
        if (u.AbsolutePath == "/" && !t.EndsWith('/') && !t.Contains("/?")) clean = clean.TrimEnd('/');
        return new CameraQrResult(u.Scheme.StartsWith("rtsp") ? "rtsp" : "snapshot", clean, user, pass);
    }
}

/// <summary>Moving a camera that supports it. Builds the request; <see cref="CameraClient"/> sends it.</summary>
public sealed record PtzRequest(HttpMethod Method, Uri Uri, string? Body);

public static class CameraPtz
{
    public static readonly string[] Directions = { "left", "right", "up", "down", "zoomin", "zoomout", "stop" };

    public static bool Supports(Camera c) => CameraPresets.Get(c.Brand) is { Ptz: true } && CameraUrl.Check(c.Kind, c.Url, out _) == CameraUrlProblem.None;

    public static PtzRequest? Build(Camera c, string direction)
    {
        if (!Supports(c) || !Directions.Contains(direction) || CameraUrl.Check(c.Kind, c.Url, out var u) != CameraUrlProblem.None || u is null) return null;
        var root = u.GetLeftPart(UriPartial.Authority);
        var ch = Math.Max(1, c.Channel);
        switch (c.Brand)
        {
            case "dahua":
            {
                var code = direction switch { "left" => "Left", "right" => "Right", "up" => "Up", "down" => "Down", "zoomin" => "ZoomTele", "zoomout" => "ZoomWide", _ => "Left" };
                var action = direction == "stop" ? "stop" : "start";
                return new PtzRequest(HttpMethod.Get, new Uri($"{root}/cgi-bin/ptz.cgi?action={action}&channel={ch - 1}&code={code}&arg1=0&arg2=3&arg3=0"), null);
            }
            case "axis":
            {
                var q = direction switch
                {
                    "left" => "continuouspantiltmove=-40,0", "right" => "continuouspantiltmove=40,0", "up" => "continuouspantiltmove=0,40", "down" => "continuouspantiltmove=0,-40",
                    "zoomin" => "continuouszoommove=40", "zoomout" => "continuouszoommove=-40", _ => "continuouspantiltmove=0,0&continuouszoommove=0",
                };
                return new PtzRequest(HttpMethod.Get, new Uri($"{root}/axis-cgi/com/ptz.cgi?camera={ch}&{q}"), null);
            }
            case "hikvision":
            {
                var (pan, tilt, zoom) = direction switch { "left" => (-60, 0, 0), "right" => (60, 0, 0), "up" => (0, 60, 0), "down" => (0, -60, 0), "zoomin" => (0, 0, 60), "zoomout" => (0, 0, -60), _ => (0, 0, 0) };
                return new PtzRequest(HttpMethod.Put, new Uri($"{root}/ISAPI/PTZCtrl/channels/{ch}/continuous"), $"<PTZData><pan>{pan}</pan><tilt>{tilt}</tilt><zoom>{zoom}</zoom></PTZData>");
            }
            default: return null;
        }
    }
}

/// <summary>Finds devices on the user's own network that might be cameras: a quick look at a few common ports. Only ever aimed at private addresses.</summary>
public sealed record ScanResult(string Host, int[] OpenPorts, string? Hint, string? PresetGuess, bool Likely);

public static class CameraScanner
{
    public static readonly int[] DefaultPorts = { 80, 8080, 88, 554, 8000, 8899 };

    /// <summary>True for addresses in the private ranges (10.x, 172.16-31.x, 192.168.x) and the Tailscale range.</summary>
    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] is >= 64 and <= 127);
    }

    /// <summary>The other 253 addresses of the /24 network around a local address. Empty for a non-private address.</summary>
    public static List<string> Subnet24Hosts(string localIp)
    {
        if (!IPAddress.TryParse(localIp, out var ip) || !IsPrivate(ip)) return new();
        var b = ip.GetAddressBytes();
        return Enumerable.Range(1, 254).Where(i => i != b[3]).Select(i => $"{b[0]}.{b[1]}.{b[2]}.{i}").ToList();
    }

    public static async Task<List<ScanResult>> ScanAsync(IEnumerable<string> hosts, IReadOnlyList<int>? ports = null, HttpClient? probe = null, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        ports ??= DefaultPorts;
        var list = hosts.Where(h => IPAddress.TryParse(h, out var ip) && (IPAddress.IsLoopback(ip) || IsPrivate(ip))).ToList();     // never scan public addresses
        var results = new List<ScanResult>(); var gate = new SemaphoreSlim(96); var done = 0;
        await Task.WhenAll(list.Select(async host =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var open = new List<int>();
                foreach (var p in ports) if (await IsOpenAsync(host, p, ct)) open.Add(p);
                if (open.Count > 0)
                {
                    string? hint = null;
                    if (probe is not null)
                        foreach (var p in open.Where(p => p is 80 or 8080 or 88 or 8000)) { hint = await ProbeAsync(probe, host, p, ct); if (hint is not null) break; }
                    var guess = CameraPresets.Guess(hint);
                    var likely = open.Contains(554) || guess is not null || (hint?.Contains("realm", StringComparison.OrdinalIgnoreCase) ?? false);
                    lock (results) results.Add(new ScanResult(host, open.ToArray(), hint, guess, likely));
                }
            }
            finally { gate.Release(); progress?.Report(Interlocked.Increment(ref done)); }
        }));
        return results.OrderByDescending(r => r.Likely).ThenBy(r => r.Host, StringComparer.Ordinal).ToList();
    }

    private static async Task<bool> IsOpenAsync(string host, int port, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(450);
        try { using var c = new TcpClient(); await c.ConnectAsync(host, port, cts.Token); return true; }
        catch { return false; }
    }

    /// <summary>One look at the device's web page for its server name or login realm (no password is sent).</summary>
    private static async Task<string?> ProbeAsync(HttpClient http, string host, int port, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(1500);
        try
        {
            using var r = await http.GetAsync($"http://{host}:{port}/", HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var server = string.Join(" ", r.Headers.Server.Select(s => s.ToString()));
            var auth = r.Headers.WwwAuthenticate.FirstOrDefault()?.ToString();
            var text = string.Join(" ", new[] { server, auth }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return text.Length == 0 ? null : text;
        }
        catch { return null; }
    }
}
