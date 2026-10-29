using GardenHelper.Core.Greenhouse;
using GardenHelper.Core.Services;
using Xunit;

namespace GardenHelper.Tests;

public class GreenhouseTests
{
    private static readonly GreenhouseSetup Soil = new() { System = "soil", TempMax = 28, TempMin = 5, HumidityMax = 85, SoilMoistureMin = 30 };
    private static Dictionary<string, double> L(params (string, double)[] v) => v.ToDictionary(x => x.Item1, x => x.Item2);
    private static readonly IReadOnlyDictionary<string, bool> Off = new Dictionary<string, bool> { ["vent"] = false, ["fan"] = false, ["water"] = false };

    [Fact]
    public void Vent_opens_when_hot_and_closes_only_after_cooling_with_margin()
    {
        var open = GreenhouseRules.Decide(Soil, L(("air_temp", 30)), Off);
        Assert.True(open["vent"]);
        var stillOpen = GreenhouseRules.Decide(Soil, L(("air_temp", 27.5)), open);      // below the limit but inside the margin
        Assert.True(stillOpen["vent"]);
        var closed = GreenhouseRules.Decide(Soil, L(("air_temp", 25)), open);
        Assert.False(closed["vent"]);
    }

    [Fact]
    public void Humidity_opens_the_vent_unless_it_is_nearly_freezing()
    {
        Assert.True(GreenhouseRules.Decide(Soil, L(("air_temp", 18), ("humidity", 92)), Off)["vent"]);
        Assert.False(GreenhouseRules.Decide(Soil, L(("air_temp", 6), ("humidity", 92)), Off)["vent"]);
    }

    [Fact]
    public void Fan_needs_real_heat_and_water_follows_soil_moisture_with_margin()
    {
        Assert.False(GreenhouseRules.Decide(Soil, L(("air_temp", 30)), Off)["fan"]);
        Assert.True(GreenhouseRules.Decide(Soil, L(("air_temp", 32)), Off)["fan"]);
        var wet = GreenhouseRules.Decide(Soil, L(("soil_moisture", 20)), Off);
        Assert.True(wet["water"]);
        Assert.True(GreenhouseRules.Decide(Soil, L(("soil_moisture", 35)), wet)["water"]);      // not wet enough yet
        Assert.False(GreenhouseRules.Decide(Soil, L(("soil_moisture", 41)), wet)["water"]);
    }

    [Fact]
    public void No_reading_changes_nothing()
    {
        var cur = new Dictionary<string, bool> { ["vent"] = true, ["fan"] = true, ["water"] = true };
        var next = GreenhouseRules.Decide(Soil, L(), cur);
        Assert.Equal(cur, next);
    }

    [Fact]
    public void Alerts_cover_frost_heat_stale_data_and_fish_water()
    {
        var now = DateTime.UtcNow;
        Assert.Equal("gh_alert_no_data", GreenhouseRules.Evaluate(Soil, L(), null, now).Single().Key);
        Assert.Contains(GreenhouseRules.Evaluate(Soil, L(("air_temp", -2)), now, now), a => a.Key == "gh_alert_frost" && a.Severity == "critical");
        Assert.Contains(GreenhouseRules.Evaluate(Soil, L(("air_temp", 40)), now, now), a => a.Key == "gh_alert_hot");
        Assert.Contains(GreenhouseRules.Evaluate(Soil, L(("air_temp", 20)), now.AddMinutes(-40), now), a => a.Key == "gh_alert_stale");
        Assert.DoesNotContain(GreenhouseRules.Evaluate(Soil, L(("air_temp", 20), ("humidity", 60)), now, now), a => a.Severity != "info");
        var fish = new GreenhouseSetup { System = "aquaponic", WaterTempMax = 26, PhMin = 6.5, PhMax = 7.5 };
        var alerts = GreenhouseRules.Evaluate(fish, L(("water_temp", 29), ("ph", 5.9), ("water_level", 10)), now, now).Select(a => a.Key).ToList();
        Assert.Contains("gh_alert_water_hot_fish", alerts); Assert.Contains("gh_alert_ph", alerts); Assert.Contains("gh_alert_level", alerts);
        Assert.DoesNotContain(GreenhouseRules.Evaluate(Soil, L(("ph", 2), ("water_temp", 50)), now, now), a => a.Key is "gh_alert_ph" or "gh_alert_water_hot");   // not relevant to soil
    }

    [Fact]
    public void Implausible_values_are_rejected()
    {
        Assert.True(GreenhouseRules.Plausible("air_temp", 21.5));
        Assert.False(GreenhouseRules.Plausible("air_temp", 400));
        Assert.False(GreenhouseRules.Plausible("humidity", double.NaN));
        Assert.False(GreenhouseRules.Plausible("secret_metric", 1));
    }

    [Fact]
    public async Task Ingest_stores_good_values_and_drives_automatic_switches()
    {
        using var env = await TestEnv.CreateAsync();
        var svc = new GreenhouseService(new Repository(env.Factory));
        var kept = await svc.IngestAsync(1, new Dictionary<string, double> { ["air_temp"] = 31, ["humidity"] = 50, ["bogus"] = 5, ["ph"] = 99 }, DateTime.UtcNow);
        Assert.Equal(2, kept);
        Assert.True((await svc.CommandsAsync(1))["vent"]);
        var latest = await svc.LatestAsync(1);
        Assert.Equal(31, latest["air_temp"].Value);
        Assert.False(latest.ContainsKey("bogus"));
    }

    [Fact]
    public async Task Manual_switches_are_not_overridden_by_the_rules()
    {
        using var env = await TestEnv.CreateAsync();
        var svc = new GreenhouseService(new Repository(env.Factory));
        await svc.SetActuatorAsync(2, "vent", desired: false, auto: false);        // held shut by hand
        await svc.IngestAsync(2, new Dictionary<string, double> { ["air_temp"] = 35 }, DateTime.UtcNow);
        Assert.False((await svc.CommandsAsync(2))["vent"]);
        await svc.SetActuatorAsync(2, "not_a_switch", true, true);
        Assert.False((await svc.CommandsAsync(2)).ContainsKey("not_a_switch"));
    }
}
