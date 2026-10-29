using GardenHelper.Core.Services;

namespace GardenHelper.Core.Greenhouse;

/// <summary>Thresholds and growing system of one greenhouse (a PropertyFeature of kind greenhouse).</summary>
public class GreenhouseSetup
{
    public int Id { get; set; }
    public int FeatureId { get; set; }
    /// <summary>soil, hydroponic or aquaponic.</summary>
    public string System { get; set; } = "soil";
    public double TempMax { get; set; } = 28;
    public double TempMin { get; set; } = 5;
    public double HumidityMax { get; set; } = 85;
    public double SoilMoistureMin { get; set; } = 30;
    public double WaterTempMax { get; set; } = 26;
    public double PhMin { get; set; } = 5.8;
    public double PhMax { get; set; } = 7.2;
    /// <summary>When on, vents, fans and watering follow the thresholds. Each switch can still be set to manual.</summary>
    public bool AutoControl { get; set; } = true;
    public double WidthCm { get; set; } = 600;
    public double LengthCm { get; set; } = 300;
}

public class GreenhouseReading
{
    public int Id { get; set; }
    public int FeatureId { get; set; }
    public string Metric { get; set; } = "";
    public double Value { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A switch the Raspberry Pi drives: vent, fan, water valve, pump or light. <see cref="Desired"/> is what the Pi should do.</summary>
public class GreenhouseActuator
{
    public int Id { get; set; }
    public int FeatureId { get; set; }
    public string Key { get; set; } = "vent";
    public bool Desired { get; set; }
    /// <summary>Auto = follows the rules; manual = stays as set by hand.</summary>
    public bool Auto { get; set; } = true;
    public DateTime ChangedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Something placed inside the greenhouse layout (bench, grow bed, tank, sensor...). Sizes and positions are centimetres.</summary>
public class GreenhouseItem
{
    public int Id { get; set; }
    public int FeatureId { get; set; }
    public string Kind { get; set; } = "bench";
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 100;
    public double Height { get; set; } = 50;
    public string? Notes { get; set; }
}

public sealed record MetricInfo(string Key, string Unit, double Min, double Max);
public sealed record GreenhouseAlert(string Key, string Severity, object[] Args);

public static class GreenhouseCatalog
{
    public static readonly string[] Systems = { "soil", "hydroponic", "aquaponic" };
    public static readonly string[] ActuatorKeys = { "vent", "fan", "water", "pump", "light" };
    public static readonly string[] ItemKinds = { "bench", "growbed", "pots", "seedtray", "shelf", "channel", "tank", "sink", "sensor", "light", "fan", "heater", "mister", "vent", "door", "pump", "path", "other" };

    public static readonly MetricInfo[] Metrics =
    {
        new("air_temp", "°C", -40, 80), new("humidity", "%", 0, 100), new("soil_moisture", "%", 0, 100), new("light", "lx", 0, 200000),
        new("water_temp", "°C", -5, 60), new("ph", "pH", 0, 14), new("ec", "mS/cm", 0, 20), new("water_level", "%", 0, 100),
    };

    public static MetricInfo? Metric(string key) => Metrics.FirstOrDefault(m => m.Key == key);

    /// <summary>Defaults that suit the growing system: fish and plants share water in aquaponics, so pH stays near neutral.</summary>
    public static void ApplyDefaults(GreenhouseSetup s)
    {
        switch (s.System)
        {
            case "aquaponic": s.PhMin = 6.5; s.PhMax = 7.5; s.WaterTempMax = 26; break;
            case "hydroponic": s.PhMin = 5.5; s.PhMax = 6.5; s.WaterTempMax = 24; break;
            default: s.PhMin = 5.8; s.PhMax = 7.2; s.WaterTempMax = 26; break;
        }
    }
}

public static class GreenhouseRules
{
    private static bool Has(IReadOnlyDictionary<string, double> v, string k, out double x) => v.TryGetValue(k, out x);

    /// <summary>The state each automatic switch should take. Uses a margin so a switch does not flick on and off around its limit.</summary>
    public static Dictionary<string, bool> Decide(GreenhouseSetup s, IReadOnlyDictionary<string, double> latest, IReadOnlyDictionary<string, bool> current)
    {
        var next = new Dictionary<string, bool>(current);
        if (Has(latest, "air_temp", out var t))
        {
            var humid = Has(latest, "humidity", out var h) ? h : double.NaN;
            var tooHumid = !double.IsNaN(humid) && humid > s.HumidityMax;
            var humidOk = double.IsNaN(humid) || humid < s.HumidityMax - 5;
            if (t > s.TempMax || tooHumid) next["vent"] = t > s.TempMin + 3 || t > s.TempMax;     // too humid but nearly freezing: stay shut
            else if (t < s.TempMax - 2 && humidOk) next["vent"] = false;
            if (t > s.TempMax + 3) next["fan"] = true; else if (t < s.TempMax) next["fan"] = false;
        }
        if (s.System == "soil" && Has(latest, "soil_moisture", out var m))
        {
            if (m < s.SoilMoistureMin) next["water"] = true; else if (m >= s.SoilMoistureMin + 10) next["water"] = false;
        }
        else if (s.System == "soil" && !current.ContainsKey("water")) next["water"] = false;
        return next;
    }

    /// <summary>Problems worth telling you about. Severity: critical, warning or info.</summary>
    public static List<GreenhouseAlert> Evaluate(GreenhouseSetup s, IReadOnlyDictionary<string, double> latest, DateTime? lastReadingUtc, DateTime nowUtc)
    {
        var a = new List<GreenhouseAlert>();
        if (lastReadingUtc is null) { a.Add(new("gh_alert_no_data", "info", Array.Empty<object>())); return a; }
        var ageMin = (nowUtc - lastReadingUtc.Value).TotalMinutes;
        if (ageMin > 15) a.Add(new("gh_alert_stale", "warning", new object[] { Math.Round(ageMin) }));
        if (Has(latest, "air_temp", out var t))
        {
            if (t < 0) a.Add(new("gh_alert_frost", "critical", new object[] { t.ToString("0.#") }));
            else if (t < s.TempMin) a.Add(new("gh_alert_cold", "warning", new object[] { t.ToString("0.#"), s.TempMin.ToString("0.#") }));
            if (t > s.TempMax + 5) a.Add(new("gh_alert_hot", "critical", new object[] { t.ToString("0.#") }));
        }
        if (Has(latest, "humidity", out var h) && h > s.HumidityMax) a.Add(new("gh_alert_humid", "warning", new object[] { h.ToString("0") }));
        if (s.System == "soil" && Has(latest, "soil_moisture", out var m) && m < s.SoilMoistureMin - 10) a.Add(new("gh_alert_dry", "warning", new object[] { m.ToString("0") }));
        if (s.System != "soil")
        {
            if (Has(latest, "water_temp", out var wt) && wt > s.WaterTempMax) a.Add(new(s.System == "aquaponic" ? "gh_alert_water_hot_fish" : "gh_alert_water_hot", "critical", new object[] { wt.ToString("0.#"), s.WaterTempMax.ToString("0.#") }));
            if (Has(latest, "ph", out var ph) && (ph < s.PhMin || ph > s.PhMax)) a.Add(new("gh_alert_ph", "warning", new object[] { ph.ToString("0.0"), s.PhMin.ToString("0.0"), s.PhMax.ToString("0.0") }));
            if (Has(latest, "water_level", out var wl) && wl < 20) a.Add(new("gh_alert_level", "critical", new object[] { wl.ToString("0") }));
        }
        return a;
    }

    public static bool Plausible(string metric, double value) =>
        GreenhouseCatalog.Metric(metric) is { } info && double.IsFinite(value) && value >= info.Min && value <= info.Max;
}

/// <summary>Stores what the Raspberry Pi reports and works out what it should switch.</summary>
public sealed class GreenhouseService
{
    private readonly Repository _repo;
    public GreenhouseService(Repository repo) => _repo = repo;

    public async Task<GreenhouseSetup> SetupAsync(int featureId, CancellationToken ct = default)
    {
        var s = (await _repo.ListAsync<GreenhouseSetup>(q => q.Where(x => x.FeatureId == featureId), ct)).FirstOrDefault();
        return s ?? await _repo.SaveAsync(new GreenhouseSetup { FeatureId = featureId }, ct);
    }

    public Task<List<GreenhouseActuator>> ActuatorsAsync(int featureId, CancellationToken ct = default) =>
        _repo.ListAsync<GreenhouseActuator>(q => q.Where(x => x.FeatureId == featureId), ct);

    /// <summary>Newest value of each metric.</summary>
    public async Task<Dictionary<string, (double Value, DateTime AtUtc)>> LatestAsync(int featureId, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-2);
        var rows = await _repo.ListAsync<GreenhouseReading>(q => q.Where(r => r.FeatureId == featureId && r.AtUtc > since).OrderByDescending(r => r.AtUtc).Take(500), ct);
        return rows.GroupBy(r => r.Metric).ToDictionary(g => g.Key, g => (g.First().Value, g.First().AtUtc));
    }

    /// <summary>Saves readings from the Pi (unknown or impossible values are ignored) and updates the automatic switches. Returns how many values were kept.</summary>
    public async Task<int> IngestAsync(int featureId, IReadOnlyDictionary<string, double> values, DateTime nowUtc, CancellationToken ct = default, bool applyRules = true)
    {
        var kept = values.Where(kv => GreenhouseRules.Plausible(kv.Key, kv.Value)).ToList();
        foreach (var kv in kept) await _repo.SaveAsync(new GreenhouseReading { FeatureId = featureId, Metric = kv.Key, Value = kv.Value, AtUtc = nowUtc }, ct);
        if (kept.Count > 0 && applyRules) await ApplyRulesAsync(featureId, ct);
        return kept.Count;
    }

    public async Task ApplyRulesAsync(int featureId, CancellationToken ct = default)
    {
        var setup = await SetupAsync(featureId, ct);
        if (!setup.AutoControl) return;
        var latest = (await LatestAsync(featureId, ct)).ToDictionary(kv => kv.Key, kv => kv.Value.Value);
        var acts = await ActuatorsAsync(featureId, ct);
        var current = acts.ToDictionary(a => a.Key, a => a.Desired);
        var next = GreenhouseRules.Decide(setup, latest, current);
        foreach (var (key, desired) in next)
        {
            var existing = acts.FirstOrDefault(a => a.Key == key);
            if (existing is null) await _repo.SaveAsync(new GreenhouseActuator { FeatureId = featureId, Key = key, Desired = desired }, ct);
            else if (existing.Auto && existing.Desired != desired) { existing.Desired = desired; existing.ChangedUtc = DateTime.UtcNow; await _repo.SaveAsync(existing, ct); }
        }
    }

    public async Task SetActuatorAsync(int featureId, string key, bool desired, bool auto, CancellationToken ct = default)
    {
        if (!GreenhouseCatalog.ActuatorKeys.Contains(key)) return;
        var a = (await ActuatorsAsync(featureId, ct)).FirstOrDefault(x => x.Key == key) ?? new GreenhouseActuator { FeatureId = featureId, Key = key };
        a.Desired = desired; a.Auto = auto; a.ChangedUtc = DateTime.UtcNow;
        await _repo.SaveAsync(a, ct);
    }

    /// <summary>What the Pi should do right now: key to on/off.</summary>
    public async Task<Dictionary<string, bool>> CommandsAsync(int featureId, CancellationToken ct = default) =>
        (await ActuatorsAsync(featureId, ct)).ToDictionary(a => a.Key, a => a.Desired);

    /// <summary>Removes readings older than 30 days so the database stays small.</summary>
    public async Task PruneAsync(CancellationToken ct = default)
    {
        var cut = DateTime.UtcNow.AddDays(-30);
        var old = await _repo.ListAsync<GreenhouseReading>(q => q.Where(r => r.AtUtc < cut).Take(5000), ct);
        foreach (var r in old) await _repo.DeleteAsync(r, ct);
    }
}
