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
