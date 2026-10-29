using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Planning;
using Xunit;

namespace GardenHelper.Tests;

public class TodayTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);

    [Fact]
    public void Late_task_is_urgent_after_three_days_and_due_today_is_soon()
    {
        var items = TodayPlanner.Build(Today, new[]
        {
            new GardenTask { Id = 1, Title = "Old", DueDate = Today.AddDays(-5) }, new GardenTask { Id = 2, Title = "Now", DueDate = Today },
            new GardenTask { Id = 3, Title = "Later", DueDate = Today.AddDays(2) }, new GardenTask { Id = 4, Title = "Finished", DueDate = Today.AddDays(-9), Done = true },
        }, Array.Empty<InventoryPlant>(), Array.Empty<AnimalGroup>());
        Assert.Equal(2, items.Count);
        Assert.Equal("urgent", items[0].Severity); Assert.Equal(1, items[0].RefId);
        Assert.Equal("soon", items[1].Severity);
    }

    [Fact]
    public void Overdue_watering_is_listed_and_trees_and_dead_plants_are_not()
    {
        var plants = new[]
        {
            new InventoryPlant { Id = 1, Name = "Monstera", WateringIntervalDays = 7, LastWatered = Today.AddDays(-9) },
            new InventoryPlant { Id = 2, Name = "Fresh", WateringIntervalDays = 7, LastWatered = Today.AddDays(-1) },
            new InventoryPlant { Id = 3, Name = "Tree", IsTree = true, WateringIntervalDays = 7, LastWatered = Today.AddDays(-30) },
            new InventoryPlant { Id = 4, Name = "Gone", Status = "dead", WateringIntervalDays = 7, LastWatered = Today.AddDays(-30) },
        };
        var items = TodayPlanner.Build(Today, Array.Empty<GardenTask>(), plants, Array.Empty<AnimalGroup>());
        var one = Assert.Single(items);
        Assert.Equal(TodayAction.Watered, one.Action); Assert.Equal(1, one.RefId);
    }

    [Fact]
    public void Eggs_are_asked_for_only_when_the_flock_is_tracked_and_nothing_was_logged_today()
    {
        AnimalGroup Flock(DateOnly? last) => new() { Id = 7, Name = "Hens", Species = "chicken", Logs = last is null ? new() : new() { new AnimalLog { Kind = "production", Date = last.Value, Quantity = 9 } } };
        Assert.Empty(TodayPlanner.Build(Today, Array.Empty<GardenTask>(), Array.Empty<InventoryPlant>(), new[] { Flock(null) }));
        Assert.Empty(TodayPlanner.Build(Today, Array.Empty<GardenTask>(), Array.Empty<InventoryPlant>(), new[] { Flock(Today) }));
        var ask = Assert.Single(TodayPlanner.Build(Today, Array.Empty<GardenTask>(), Array.Empty<InventoryPlant>(), new[] { Flock(Today.AddDays(-1)) }));
        Assert.True(ask.NeedsNumber); Assert.Equal("eggs", ask.Unit);
    }

    [Fact]
    public void Active_withdrawal_is_reported()
    {
        var g = new AnimalGroup { Id = 2, Name = "Goats", Species = "goat", Logs = new() { new AnimalLog { Kind = "treatment", Date = Today.AddDays(-4), WithdrawalUntil = Today.AddDays(10) } } };
        var items = TodayPlanner.Build(Today, Array.Empty<GardenTask>(), Array.Empty<InventoryPlant>(), new[] { g });
        Assert.Contains(items, i => i.Key == "today_withdrawal");
    }
}

public class TodayExtrasTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);

    [Fact]
    public void Frost_tomorrow_is_urgent_and_rain_today_is_a_tip()
    {
        var days = new[]
        {
            new GardenHelper.Core.Site.WeatherDay(Today, 4, 12, 8, 80, 20, 61),
            new GardenHelper.Core.Site.WeatherDay(Today.AddDays(1), -2, 9, 0, 10, 10, 0),
            new GardenHelper.Core.Site.WeatherDay(Today.AddDays(6), -5, 9, 0, 10, 10, 0),
        };
        var items = GardenHelper.Core.Planning.WeatherAdvice.Evaluate(days, Today, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(items, i => i.Key == "today_wx_frost" && i.Severity == "urgent");
        Assert.Contains(items, i => i.Key == "today_wx_rain");
        Assert.Equal(1, items.Count(i => i.Key == "today_wx_frost"));      // day 6 is beyond the three-day window
    }
}


public class WateringAdvisorTests
{
    private static readonly DateOnly Today = new(2026, 7, 10);
    private static readonly System.Globalization.CultureInfo C = System.Globalization.CultureInfo.InvariantCulture;
    private static GardenHelper.Core.Site.WeatherDay Day(int offset, double max, double mm = 0, int prob = 0) => new(Today.AddDays(offset), 12, max, mm, prob, 10, 0);

    [Fact]
    public void Dry_soil_asks_for_water_unless_rain_is_coming()
    {
        var beds = new[] { new GardenHelper.Core.Planning.BedWatering(1, "Bed A", 12, null, true), new GardenHelper.Core.Planning.BedWatering(2, "Bed B", 60, null, true), new GardenHelper.Core.Planning.BedWatering(3, "Empty", 5, null, false) };
        var dry = GardenHelper.Core.Planning.WateringAdvisor.Evaluate(beds, new[] { Day(0, 24) }, Today, C);
        var one = Assert.Single(dry); Assert.Equal("urgent", one.Severity); Assert.Equal(GardenHelper.Core.Planning.TodayAction.WaterBed, one.Action);
        var wet = GardenHelper.Core.Planning.WateringAdvisor.Evaluate(beds, new[] { Day(0, 24, 9, 80) }, Today, C);
        Assert.Equal("today_bed_dry_rain", Assert.Single(wet).Key);
    }

    [Fact]
    public void Without_a_sensor_only_hot_dry_spells_trigger_a_reminder()
    {
        var beds = new[] { new GardenHelper.Core.Planning.BedWatering(1, "Bed A", null, Today.AddDays(-4), true), new GardenHelper.Core.Planning.BedWatering(2, "Bed B", null, Today.AddDays(-1), true) };
        Assert.Empty(GardenHelper.Core.Planning.WateringAdvisor.Evaluate(beds, new[] { Day(0, 22) }, Today, C));
        var hot = GardenHelper.Core.Planning.WateringAdvisor.Evaluate(beds, new[] { Day(0, 31) }, Today, C);
        Assert.Equal("today_bed_hot", Assert.Single(hot).Key);
        Assert.Empty(GardenHelper.Core.Planning.WateringAdvisor.Evaluate(beds, new[] { Day(0, 31, 7, 70) }, Today, C));
    }
}
