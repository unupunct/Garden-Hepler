using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;

namespace GardenHelper.Core.Planning;

/// <summary>Money in or out that is not already recorded elsewhere (animal costs are read from the animal history, not typed twice).</summary>
public class BudgetEntry
{
    public int Id { get; set; }
    public int GardenId { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string Category { get; set; } = "other";
    /// <summary>Always positive; <see cref="IsIncome"/> says which way it goes.</summary>
    public decimal Amount { get; set; }
    public bool IsIncome { get; set; }
    public string Text { get; set; } = "";
}

public static class BudgetCatalog
{
    public static readonly string[] Categories = { "seeds", "plants", "tools", "feed", "vet", "utilities", "infrastructure", "sales", "other" };
    /// <summary>Costs taken from the Animals tab are shown under this key.</summary>
    public const string AnimalsKey = "animals";
}

public sealed record CategoryTotal(string Category, decimal Amount);
public sealed record BudgetSummary(decimal Income, decimal Expenses, decimal Net, IReadOnlyList<CategoryTotal> ExpenseByCategory);

public static class BudgetRules
{
    private static bool InMonth(DateOnly d, int year, int month) => d.Year == year && d.Month == month;

    /// <summary>Month totals. Animal expenses and treatment costs are added as one "animals" expense category.</summary>
    public static BudgetSummary Summarize(IEnumerable<BudgetEntry> entries, IEnumerable<AnimalLog> animalLogs, int year, int month)
    {
        var inMonth = entries.Where(e => InMonth(e.Date, year, month) && e.Amount > 0).ToList();
        var income = inMonth.Where(e => e.IsIncome).Sum(e => e.Amount);
        var byCat = inMonth.Where(e => !e.IsIncome).GroupBy(e => e.Category).Select(g => new CategoryTotal(g.Key, g.Sum(e => e.Amount))).ToList();
        var animals = AnimalRules.SpentInMonth(animalLogs, year, month);
        if (animals > 0) byCat.Add(new CategoryTotal(BudgetCatalog.AnimalsKey, animals));
        var expenses = byCat.Sum(c => c.Amount);
        return new(income, expenses, income - expenses, byCat.OrderByDescending(c => c.Amount).ToList());
    }

    public static (int Year, int Month) AddMonths(int year, int month, int delta)
    {
        var d = new DateOnly(year, month, 1).AddMonths(delta);
        return (d.Year, d.Month);
    }
}

public sealed record UpcomingItem(DateOnly Date, string Kind, string Title, string? Detail, int? RefId);

/// <summary>Everything that is due soon, from tasks and animal treatments, grouped by how far away it is.</summary>
public static class ComingUpPlanner
{
    public static readonly string[] Buckets = { "overdue", "today", "week", "month", "later" };

    public static string BucketOf(DateOnly date, DateOnly today) =>
        date < today ? "overdue" : date == today ? "today" : date <= today.AddDays(7) ? "week" : date <= today.AddDays(31) ? "month" : "later";

    /// <summary>Open tasks (including overdue) and the end of each treatment withdrawal, up to <paramref name="horizonDays"/> days ahead, soonest first.</summary>
    public static List<UpcomingItem> Build(IEnumerable<GardenTask> tasks, IEnumerable<(AnimalLog Log, string Group)> treatments, DateOnly today, int horizonDays)
    {
        var last = today.AddDays(horizonDays);
        var items = new List<UpcomingItem>();
        items.AddRange(tasks.Where(t => !t.Done && t.DueDate <= last).Select(t => new UpcomingItem(t.DueDate, "task", t.Title, t.Notes, t.Id)));
        foreach (var (log, group) in treatments)
        {
            if (log.Kind != "treatment" || log.WithdrawalUntil is not { } w) continue;
            var safe = w.AddDays(1);      // the withdrawal day itself is still unsafe
            if (safe > today && safe <= last) items.Add(new UpcomingItem(safe, "withdrawal", group, log.Text, log.GroupId));
        }
        return items.OrderBy(i => i.Date).ThenBy(i => i.Kind).ThenBy(i => i.Title).ToList();
    }
}
