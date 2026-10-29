namespace GardenHelper.Core.Animals;

/// <summary>A group of animals kept together (30 laying hens, 2 pigs, a few goats), usually living in one place on the property.</summary>
public class AnimalGroup
{
    public int Id { get; set; }
    public int GardenId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>One of <see cref="AnimalCatalog.Species"/>.</summary>
    public string Species { get; set; } = "chicken";
    public string? Breed { get; set; }
    public int Count { get; set; } = 1;
    /// <summary>The coop, pen or barn (a PropertyFeature) where they live.</summary>
    public int? FeatureId { get; set; }
    public string? Notes { get; set; }
    public List<FeedRation> Rations { get; set; } = new();
    public List<AnimalLog> Logs { get; set; } = new();
}

/// <summary>What the group eats: grams per animal per day, and what a kilogram costs.</summary>
public class FeedRation
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public AnimalGroup? Group { get; set; }
    public string Name { get; set; } = "";
    public double GramsPerAnimalPerDay { get; set; }
    /// <summary>How much of this feed is in store (kg) on <see cref="StockDate"/>; empty when not tracked.</summary>
    public double? StockKg { get; set; }
    public DateOnly? StockDate { get; set; }
    public decimal PricePerKg { get; set; }
}

/// <summary>A dated entry: production (eggs, milk), treatment, expense, count change, note.</summary>
public class AnimalLog
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public AnimalGroup? Group { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    /// <summary>production, treatment, expense, count, note.</summary>
    public string Kind { get; set; } = "note";
    public string Text { get; set; } = "";
    /// <summary>Eggs or litres for production; animals added (+) or lost (-) for count.</summary>
    public double? Quantity { get; set; }
    public decimal? Cost { get; set; }
    /// <summary>Treatments: eggs, milk and meat must not be used before this day.</summary>
    public DateOnly? WithdrawalUntil { get; set; }
    /// <summary>When the next vaccination or worming is due, or when young are expected (breeding).</summary>
    public DateOnly? DueDate { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public static class AnimalCatalog
{
    public static readonly string[] Species = { "chicken", "duck", "rabbit", "goat", "pig", "other" };
    public static readonly string[] LogKinds = { "production", "treatment", "expense", "count", "note", "weight", "breeding", "vaccination", "worming" };
}

public sealed record AnimalSummary(double FeedKgPerDay, decimal FeedCostPerDay, decimal FeedCostPerMonth, double? AveragePerDay, double? LayingPercent, DateOnly? WithdrawalUntil);

public static class AnimalRules
{
    public static double FeedKgPerDay(int count, IEnumerable<FeedRation> rations) => rations.Sum(r => Math.Max(0, count) * Math.Max(0, r.GramsPerAnimalPerDay)) / 1000.0;

    public static decimal FeedCostPerDay(int count, IEnumerable<FeedRation> rations) =>
        rations.Sum(r => (decimal)(Math.Max(0, count) * Math.Max(0, r.GramsPerAnimalPerDay) / 1000.0) * Math.Max(0, r.PricePerKg));

    /// <summary>Average production per day over the last <paramref name="days"/> days that have an entry. Null when nothing was logged.</summary>
    public static double? AveragePerDay(IEnumerable<AnimalLog> logs, DateOnly today, int days = 7)
    {
        var from = today.AddDays(-(days - 1));
        var byDay = logs.Where(l => l.Kind == "production" && l.Quantity is not null && l.Date >= from && l.Date <= today)
                        .GroupBy(l => l.Date).Select(g => g.Sum(l => l.Quantity!.Value)).ToList();
        return byDay.Count == 0 ? null : byDay.Average();
    }

    /// <summary>Eggs per animal as a percentage (a healthy laying flock is roughly 60-90%).</summary>
    public static double? LayingPercent(double? perDay, int count) => perDay is null || count <= 0 ? null : perDay.Value / count * 100;

    /// <summary>The latest day (today or later) until which products must not be used, or null.</summary>
    public static DateOnly? ActiveWithdrawal(IEnumerable<AnimalLog> logs, DateOnly today) =>
        logs.Where(l => l.Kind == "treatment" && l.WithdrawalUntil is { } w && w >= today).Select(l => l.WithdrawalUntil).Max();

    /// <summary>Days from mating (or setting eggs) to birth or hatching.</summary>
    public static int Gestation(string species) => species switch { "chicken" => 21, "duck" => 28, "rabbit" => 31, "goat" => 150, "pig" => 114, _ => 0 };

    /// <summary>Days until a feed in store runs out at the current ration, or null when stock is not tracked.</summary>
    public static double? FeedDaysLeft(FeedRation ration, int count, DateOnly today)
    {
        if (ration.StockKg is not { } kg || ration.StockDate is not { } on) return null;
        var daily = Math.Max(0, count) * Math.Max(0, ration.GramsPerAnimalPerDay) / 1000.0;
        if (daily <= 0) return null;
        var left = kg - daily * Math.Max(0, today.DayNumber - on.DayNumber);
        return Math.Max(0, left / daily);
    }

    /// <summary>The latest recorded weight (kg) and its date.</summary>
    public static (double Kg, DateOnly Date)? LastWeight(IEnumerable<AnimalLog> logs) =>
        logs.Where(l => l.Kind == "weight" && l.Quantity is > 0).OrderByDescending(l => l.Date).ThenByDescending(l => l.Id).Select(l => ((double Kg, DateOnly Date)?)(l.Quantity!.Value, l.Date)).FirstOrDefault();

    /// <summary>What is coming up for an animal group: the next vaccination or worming (latest entry of each kind that has a due date) and young that are expected soon.</summary>
    public static List<(string Kind, DateOnly Due)> UpcomingCare(IEnumerable<AnimalLog> logs, DateOnly today)
    {
        var list = new List<(string, DateOnly)>();
        foreach (var kind in new[] { "vaccination", "worming" })
            if (logs.Where(l => l.Kind == kind && l.DueDate is not null).OrderByDescending(l => l.Date).ThenByDescending(l => l.Id).FirstOrDefault() is { } last) list.Add((kind, last.DueDate!.Value));
        foreach (var b in logs.Where(l => l.Kind == "breeding" && l.DueDate is { } d && d >= today.AddDays(-3))) list.Add(("breeding", b.DueDate!.Value));
        return list;
    }

    public static int ApplyCountChange(int count, double change) => Math.Max(0, count + (int)Math.Round(change));

    public static decimal SpentInMonth(IEnumerable<AnimalLog> logs, int year, int month) =>
        logs.Where(l => (l.Kind is "expense" or "treatment") && l.Cost is not null && l.Date.Year == year && l.Date.Month == month).Sum(l => l.Cost!.Value);

    public static AnimalSummary Summarize(AnimalGroup g, DateOnly today)
    {
        var kg = FeedKgPerDay(g.Count, g.Rations); var day = FeedCostPerDay(g.Count, g.Rations);
        var avg = AveragePerDay(g.Logs, today);
        return new(kg, day, day * 30, avg, LayingPercent(avg, g.Count), ActiveWithdrawal(g.Logs, today));
    }
}
