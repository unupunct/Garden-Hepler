using System.IO;
using System.Security.Cryptography;
using System.Text;
using GardenHelper.Core.Cameras;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Planning;
using GardenHelper.Core.Greenhouse;
using GardenHelper.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.Infrastructure;

/// <summary>
/// Small web API inside the app for devices on your network (the greenhouse Raspberry Pi first; phone and Home Assistant later).
/// Off by default. Every request needs the secret token. It speaks plain HTTP, so use it on your home network or through a VPN such as Tailscale.
/// </summary>
public sealed class LocalApiServer
{
    public const string EnabledKey = "api.enabled", PortKey = "api.port", TokenKey = "api.token";
    public const int DefaultPort = 8765;

    private readonly GreenhouseService _greenhouse;
    private readonly RemoteService _remote;
    private readonly CameraClient _cameras;
    private readonly Dictionary<int, (DateTime At, byte[] Bytes)> _snapshots = new();
    private readonly CameraEvents _events;
    private readonly RecordingStore _recordings;
    private readonly CameraMonitor _monitor;
    private readonly Dictionary<int, DateTime> _lastSensor = new();
    private readonly Repository _repo;
    private readonly SettingsService _settings;
    private readonly ILogger<LocalApiServer> _log;
    private WebApplication? _app;

    public LocalApiServer(GreenhouseService greenhouse, RemoteService remote, CameraClient cameras, CameraEvents events, RecordingStore recordings, CameraMonitor monitor, Repository repo, SettingsService settings, ILogger<LocalApiServer> log)
    { _events = events; _recordings = recordings; _monitor = monitor; _greenhouse = greenhouse; _remote = remote; _cameras = cameras; _repo = repo; _settings = settings; _log = log; }

    public bool Running => _app is not null;
    public int Port { get; private set; }

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    /// <summary>The saved token (kept encrypted for this Windows account); a new one is made on first use.</summary>
    public async Task<string> TokenAsync()
    {
        var saved = CameraSecrets.Unprotect(await _settings.GetAsync(TokenKey));
        if (!string.IsNullOrEmpty(saved)) return saved;
        return await ResetTokenAsync();
    }

    public async Task<string> ResetTokenAsync()
    {
        var t = NewToken();
        await _settings.SetAsync(TokenKey, CameraSecrets.Protect(t)!);
        if (Running) { var port = Port; await StopAsync(); await StartAsync(port, t); }
        return t;
    }

    public async Task StartFromSettingsAsync()
    {
        if (await _settings.GetAsync(EnabledKey) != "1") return;
        var port = int.TryParse(await _settings.GetAsync(PortKey), out var p) && p is > 1024 and < 65536 ? p : DefaultPort;
        try { await StartAsync(port, await TokenAsync()); }
        catch (Exception ex) { _log.LogWarning(ex, "Could not start the local API on port {Port}", port); }
    }

    public async Task StartAsync(int port, string token)
    {
        if (_app is not null) return;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => { o.ListenAnyIP(port); o.AddServerHeader = false; o.Limits.MaxRequestBodySize = 16 * 1024; });
        var app = builder.Build();
        var key = Encoding.UTF8.GetBytes(token);

        app.Use(async (ctx, next) =>
        {
            // The phone page itself carries no data, so it may load without the token; it asks for the token and then calls the API.
            if (HttpMethods.IsGet(ctx.Request.Method) && PublicFiles.ContainsKey(ctx.Request.Path.Value ?? "")) { await next(); return; }
            var given = Encoding.UTF8.GetBytes(TokenFrom(ctx.Request.Headers.Authorization.ToString()));
            if (given.Length != key.Length || !CryptographicOperations.FixedTimeEquals(given, key)) { ctx.Response.StatusCode = 401; ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"Garden Helper\""; return; }
            await next();
        });

        foreach (var (path, (file, type)) in PublicFiles)
            app.MapGet(path, () => ServeFile(file, type));

        app.MapGet("/api/v1/summary", async () => Results.Json(await _remote.SummaryAsync(DateOnly.FromDateTime(DateTime.Today), DateTime.UtcNow)));
        app.MapGet("/api/v1/tasks", async () => Results.Json(await _remote.OpenTasksAsync(DateOnly.FromDateTime(DateTime.Today))));
        app.MapPost("/api/v1/tasks/{id:int}/done", async (int id) => await _remote.CompleteTaskAsync(id) ? Results.Ok(new { done = true }) : Results.NotFound(new { error = "No such task." }));
        app.MapPost("/api/v1/animals/{id:int}/production", async (int id, HttpRequest req) =>
        {
            Dictionary<string, double>? body;
            try { body = await req.ReadFromJsonAsync<Dictionary<string, double>>(); } catch { return Results.BadRequest(new { error = "Send {\"quantity\": 12}." }); }
            return body is not null && body.TryGetValue("quantity", out var q) && await _remote.AddProductionAsync(id, q, DateOnly.FromDateTime(DateTime.Today))
                ? Results.Ok(new { added = q }) : Results.BadRequest(new { error = "Unknown group or a quantity outside 0.1-10000." });
        });
        app.MapPost("/api/v1/greenhouse/{id:int}/switch/{key}", async (int id, string key, HttpRequest req) =>
        {
            Dictionary<string, bool>? body;
            try { body = await req.ReadFromJsonAsync<Dictionary<string, bool>>(); } catch { return Results.BadRequest(new { error = "Send {\"on\": true} or {\"auto\": true}." }); }
            if (body is null || !GreenhouseCatalog.ActuatorKeys.Contains(key)) return Results.BadRequest(new { error = "Unknown switch." });
            if (!await IsGreenhouseAsync(id)) return Results.NotFound(new { error = "No greenhouse with that id." });
            var current = (await _greenhouse.CommandsAsync(id)).GetValueOrDefault(key);
            if (body.GetValueOrDefault("auto")) { await _greenhouse.SetActuatorAsync(id, key, current, auto: true); await _greenhouse.ApplyRulesAsync(id); }
            else if (body.TryGetValue("on", out var on)) await _greenhouse.SetActuatorAsync(id, key, on, auto: false);
            else return Results.BadRequest(new { error = "Send \"on\" or \"auto\"." });
            return Results.Json(new { commands = await _greenhouse.CommandsAsync(id) });
        });
        app.MapGet("/api/v1/cameras", async () => Results.Json(await _remote.CamerasAsync()));
        app.MapGet("/api/v1/cameras/{id:int}/snapshot", async (int id) =>
        {
            if (_snapshots.TryGetValue(id, out var hit) && (DateTime.UtcNow - hit.At).TotalSeconds < 1) return Results.Bytes(hit.Bytes, "image/jpeg");
            var cam = (await _repo.ListAsync<Camera>(q => q.Where(c => c.Id == id && c.Enabled))).FirstOrDefault();
            if (cam is null) return Results.NotFound(new { error = "No such camera." });
            try
            {
                // The camera's own password stays on this PC; only the picture goes out.
                var bytes = await _cameras.GrabFrameAsync(cam, CameraSecrets.Unprotect(cam.SecretProtected));
                lock (_snapshots) _snapshots[id] = (DateTime.UtcNow, bytes);
                return Results.Bytes(bytes, LooksPng(bytes) ? "image/png" : "image/jpeg");
            }
            catch (CameraException ex) { return Results.Json(new { error = ex.Message }, statusCode: 502); }
        });
        // A motion sensor (a Raspberry Pi with a PIR, Home Assistant, a doorbell...) reports movement; the camera's picture is attached when it can be fetched.
        app.MapPost("/api/v1/cameras/{id:int}/events", async (int id, HttpRequest req) =>
        {
            Dictionary<string, string>? body = null;
            try { if (req.ContentLength is > 0) body = await req.ReadFromJsonAsync<Dictionary<string, string>>(); } catch { return Results.BadRequest(new { error = "Send {\"kind\": \"motion\"} or nothing." }); }
            var kind = body?.GetValueOrDefault("kind") ?? "sensor";
            if (kind is not ("sensor" or "motion")) return Results.BadRequest(new { error = "kind must be sensor or motion." });
            var cam = (await _repo.ListAsync<Camera>(q => q.Where(c => c.Id == id))).FirstOrDefault();
            if (cam is null) return Results.NotFound(new { error = "No such camera." });
            lock (_lastSensor) { if (_lastSensor.TryGetValue(id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 5) return Results.Json(new { skipped = "too soon" }, statusCode: 429); _lastSensor[id] = DateTime.UtcNow; }
            byte[]? picture = null;
            try { if (cam.Kind != "rtsp") picture = await _cameras.GrabFrameAsync(cam, CameraSecrets.Unprotect(cam.SecretProtected)); } catch (CameraException) { /* the event is still recorded without a picture */ }
            var ev = picture is not null ? await _monitor.RecordAsync(cam, kind, null, picture, DateTime.UtcNow) : await _events.AddAsync(id, kind, null, null);
            return Results.Json(new { id = ev.Id, picture = picture is not null });
        });
        app.MapGet("/api/v1/cameras/{id:int}/events", async (int id) => Results.Json((await _events.ListAsync(id, 50)).Select(e => new { e.Id, atUtc = e.AtUtc, e.Kind, e.Score, hasPicture = e.File != null })));
        app.MapGet("/api/v1/events/{id:int}/image", async (int id) =>
        {
            var e = (await _repo.ListAsync<CameraEvent>(q => q.Where(x => x.Id == id))).FirstOrDefault();
            return _recordings.PathOf(e?.File) is { } path && File.Exists(path) ? Results.File(File.ReadAllBytes(path), path.EndsWith(".png") ? "image/png" : "image/jpeg") : Results.NotFound();
        });
        app.MapGet("/api/v1/status", () => Results.Json(new { app = "Garden Helper", timeUtc = DateTime.UtcNow }));
        app.MapPost("/api/v1/greenhouse/{id:int}/readings", async (int id, HttpRequest req) =>
        {
            Dictionary<string, double>? body;
            try { body = await req.ReadFromJsonAsync<Dictionary<string, double>>(); } catch { return Results.BadRequest(new { error = "Send a JSON object such as {\"air_temp\": 21.5}." }); }
            if (body is null || body.Count > 20) return Results.BadRequest(new { error = "Send between 1 and 20 values." });
            var isHouse = await IsGreenhouseAsync(id);
            if (!isHouse && (await _repo.ListAsync<PropertyFeature>(q => q.Where(f => f.Id == id))).Count != 1) return Results.NotFound(new { error = "No place with that id." });
            var stored = await _greenhouse.IngestAsync(id, body, DateTime.UtcNow, default, applyRules: isHouse);     // outdoor places only store readings
            return Results.Json(new { stored, commands = isHouse ? await _greenhouse.CommandsAsync(id) : new Dictionary<string, bool>() });
        });
        app.MapGet("/api/v1/greenhouse/{id:int}/commands", async (int id) =>
            await IsGreenhouseAsync(id) ? Results.Json(new { commands = await _greenhouse.CommandsAsync(id) }) : Results.NotFound(new { error = "No greenhouse with that id." }));

        await app.StartAsync();
        _app = app; Port = port;
        _log.LogInformation("Local API listening on port {Port}", port);
    }

    private static bool LooksPng(byte[] b) => b.Length > 4 && b[0] == 0x89 && b[1] == 0x50;

    /// <summary>Accepts "Bearer token", or HTTP Basic with the token as the password (so Home Assistant cameras and browsers can use it).</summary>
    private static string TokenFrom(string header)
    {
        if (header.StartsWith("Bearer ", StringComparison.Ordinal)) return header[7..];
        if (header.StartsWith("Basic ", StringComparison.Ordinal))
        {
            try { var s = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..])); var i = s.IndexOf(':'); return i < 0 ? "" : s[(i + 1)..]; } catch { return ""; }
        }
        return "";
    }

    private static readonly Dictionary<string, (string File, string Type)> PublicFiles = new()
    {
        ["/"] = ("index.html", "text/html; charset=utf-8"), ["/app.js"] = ("app.js", "text/javascript; charset=utf-8"),
        ["/manifest.webmanifest"] = ("manifest.webmanifest", "application/manifest+json"), ["/icon.svg"] = ("icon.svg", "image/svg+xml"), ["/sw.js"] = ("sw.js", "text/javascript; charset=utf-8"),
    };

    private static IResult ServeFile(string file, string type)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Mobile", file);
        return File.Exists(path) ? Results.File(File.ReadAllBytes(path), type) : Results.NotFound();
    }

    private async Task<bool> IsGreenhouseAsync(int id) =>
        (await _repo.ListAsync<PropertyFeature>(q => q.Where(f => f.Id == id && f.Kind == "greenhouse"))).Count == 1;

    public async Task StopAsync()
    {
        if (_app is null) return;
        var app = _app; _app = null;
        await app.StopAsync(); await app.DisposeAsync();
    }
}
