using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Planning;
using Xunit;

namespace GardenHelper.Tests;

public class PlanningTests
{
    private static readonly DateOnly Today = new(2026, 6, 10);

    [Fact]
    public void Budget_adds_animal_costs_and_ignores_other_months_and_bad_amounts()
    {
        var entries = new[]
        {
            new BudgetEntry { Date = Today, Category = "seeds", Amount = 20 },
            new BudgetEntry { Date = Today, Category = "seeds", Amount = 5 },
            new BudgetEntry { Date = Today, Category = "tools", Amount = 60 },
            new BudgetEntry { Date = Today, Category = "sales", Amount = 45, IsIncome = true },
            new BudgetEntry { Date = Today.AddMonths(-1), Category = "tools", Amount = 999 },
            new BudgetEntry { Date = Today, Category = "other", Amount = -10 },       // invalid, ignored
        };
        var logs = new[] { new AnimalLog { Kind = "expense", Date = Today, Cost = 30m }, new AnimalLog { Kind = "treatment", Date = Today, Cost = 10m } };
        var s = BudgetRules.Summarize(entries, logs, 2026, 6);
        Assert.Equal(45, s.Income);
        Assert.Equal(125, s.Expenses);
        Assert.Equal(-80, s.Net);
        Assert.Equal(new[] { "tools", "animals", "seeds" }, s.ExpenseByCategory.Select(c => c.Category));
        Assert.Equal(25, s.ExpenseByCategory.First(c => c.Category == "seeds").Amount);
    }

    [Fact]
    public void Months_roll_over_years()
    {
        Assert.Equal((2027, 1), BudgetRules.AddMonths(2026, 12, 1));
        Assert.Equal((2025, 12), BudgetRules.AddMonths(2026, 1, -1));
    }

    [Theory]
    [InlineData(-1, "overdue")] [InlineData(0, "today")] [InlineData(1, "week")] [InlineData(7, "week")] [InlineData(8, "month")] [InlineData(31, "month")] [InlineData(32, "later")]
    public void Buckets_follow_distance(int days, string expected) => Assert.Equal(expected, ComingUpPlanner.BucketOf(Today.AddDays(days), Today));

    [Fact]
    public void Coming_up_lists_open_tasks_and_withdrawal_ends_in_order()
    {
        var tasks = new[]
        {
            new GardenTask { Id = 1, Title = "Water seedlings", DueDate = Today.AddDays(3) },
            new GardenTask { Id = 2, Title = "Late job", DueDate = Today.AddDays(-4) },
            new GardenTask { Id = 3, Title = "Finished", DueDate = Today, Done = true },
            new GardenTask { Id = 4, Title = "Far away", DueDate = Today.AddDays(200) },
        };
        var treat = new[]
        {
            (new AnimalLog { GroupId = 7, Kind = "treatment", Text = "Wormer", WithdrawalUntil = Today.AddDays(5) }, "Hens"),
            (new AnimalLog { GroupId = 7, Kind = "treatment", Text = "Old", WithdrawalUntil = Today.AddDays(-9) }, "Hens"),
            (new AnimalLog { GroupId = 8, Kind = "note", Text = "x", WithdrawalUntil = Today.AddDays(2) }, "Goats"),
        };
        var items = ComingUpPlanner.Build(tasks, treat, Today, 30);
        Assert.Equal(new[] { "Late job", "Water seedlings", "Hens" }, items.Select(i => i.Title));
        Assert.Equal(Today.AddDays(6), items[2].Date);        // safe again the day after the withdrawal ends
        Assert.Equal("withdrawal", items[2].Kind);
    }
}
