using GardenHelper.Core.Animals;
using Xunit;

namespace GardenHelper.Tests;

public class AnimalTests
{
    private static readonly DateOnly Today = new(2026, 6, 10);

    [Fact]
    public void Feed_and_cost_scale_with_head_count()
    {
        var rations = new[] { new FeedRation { GramsPerAnimalPerDay = 120, PricePerKg = 1.5m }, new FeedRation { GramsPerAnimalPerDay = 10, PricePerKg = 4m } };
        Assert.Equal(3.9, AnimalRules.FeedKgPerDay(30, rations), 6);       // 30 x 130 g
        Assert.Equal(30 * 0.12m * 1.5m + 30 * 0.01m * 4m, AnimalRules.FeedCostPerDay(30, rations));
        Assert.Equal(0, AnimalRules.FeedKgPerDay(-5, rations));
    }

    [Fact]
    public void Average_uses_only_logged_days_in_the_window()
    {
        var logs = new[]
        {
            new AnimalLog { Kind = "production", Date = Today, Quantity = 24 },
            new AnimalLog { Kind = "production", Date = Today, Quantity = 2 },          // same day adds up: 26
            new AnimalLog { Kind = "production", Date = Today.AddDays(-2), Quantity = 20 },
            new AnimalLog { Kind = "production", Date = Today.AddDays(-30), Quantity = 99 },   // outside the window
            new AnimalLog { Kind = "note", Date = Today, Quantity = 500 },                      // not production
        };
        Assert.Equal(23, AnimalRules.AveragePerDay(logs, Today));
        Assert.Null(AnimalRules.AveragePerDay(Array.Empty<AnimalLog>(), Today));
    }

    [Fact]
    public void Laying_percent_handles_empty_flock()
    {
        Assert.Equal(80, AnimalRules.LayingPercent(24, 30));
        Assert.Null(AnimalRules.LayingPercent(24, 0));
        Assert.Null(AnimalRules.LayingPercent(null, 30));
    }

    [Fact]
    public void Withdrawal_is_active_until_its_last_day()
    {
        var logs = new[]
        {
            new AnimalLog { Kind = "treatment", WithdrawalUntil = Today.AddDays(-1) },
            new AnimalLog { Kind = "treatment", WithdrawalUntil = Today },
            new AnimalLog { Kind = "treatment", WithdrawalUntil = Today.AddDays(6) },
        };
        Assert.Equal(Today.AddDays(6), AnimalRules.ActiveWithdrawal(logs, Today));
        Assert.Null(AnimalRules.ActiveWithdrawal(logs, Today.AddDays(7)));
    }

    [Fact]
    public void Count_never_goes_below_zero_and_spending_sums_by_month()
    {
        Assert.Equal(0, AnimalRules.ApplyCountChange(3, -10));
        Assert.Equal(33, AnimalRules.ApplyCountChange(30, 3));
        var logs = new[]
        {
            new AnimalLog { Kind = "expense", Date = Today, Cost = 40m }, new AnimalLog { Kind = "treatment", Date = Today, Cost = 12.5m },
            new AnimalLog { Kind = "expense", Date = Today.AddMonths(-1), Cost = 99m }, new AnimalLog { Kind = "note", Date = Today, Cost = 7m },
        };
        Assert.Equal(52.5m, AnimalRules.SpentInMonth(logs, 2026, 6));
    }
}
