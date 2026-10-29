using System.Security.Cryptography;
using System.Text;
using GardenHelper.Core.Domain;
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
    private readonly Repository _repo;
    private readonly SettingsService _settings;
    private readonly ILogger<LocalApiServer> _log;
    private WebApplication? _app;

    public LocalApiServer(GreenhouseService greenhouse, Repository repo, SettingsService settings, ILogger<LocalApiServer> log)
    { _greenhouse = greenhouse; _repo = repo; _settings = settings; _log = log; }

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
            var header = ctx.Request.Headers.Authorization.ToString();
            var given = header.StartsWith("Bearer ", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes(header[7..]) : Array.Empty<byte>();
            if (given.Length != key.Length || !CryptographicOperations.FixedTimeEquals(given, key)) { ctx.Response.StatusCode = 401; return; }
            await next();
        });

        app.MapGet("/api/v1/status", () => Results.Json(new { app = "Garden Helper", timeUtc = DateTime.UtcNow }));
        app.MapPost("/api/v1/greenhouse/{id:int}/readings", async (int id, HttpRequest req) =>
        {
            Dictionary<string, double>? body;
            try { body = await req.ReadFromJsonAsync<Dictionary<string, double>>(); } catch { return Results.BadRequest(new { error = "Send a JSON object such as {\"air_temp\": 21.5}." }); }
            if (body is null || body.Count > 20) return Results.BadRequest(new { error = "Send between 1 and 20 values." });
            if (!await IsGreenhouseAsync(id)) return Results.NotFound(new { error = "No greenhouse with that id." });
            var stored = await _greenhouse.IngestAsync(id, body, DateTime.UtcNow);
            return Results.Json(new { stored, commands = await _greenhouse.CommandsAsync(id) });
        });
        app.MapGet("/api/v1/greenhouse/{id:int}/commands", async (int id) =>
            await IsGreenhouseAsync(id) ? Results.Json(new { commands = await _greenhouse.CommandsAsync(id) }) : Results.NotFound(new { error = "No greenhouse with that id." }));

        await app.StartAsync();
        _app = app; Port = port;
        _log.LogInformation("Local API listening on port {Port}", port);
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
