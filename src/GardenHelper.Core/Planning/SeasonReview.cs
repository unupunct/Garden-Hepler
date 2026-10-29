using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;

namespace GardenHelper.Core.Planning;

public sealed record CropCount(string PlantId, int Plants, IReadOnlyList<string> Beds);
public sealed record AnimalSeason(int GroupId, string Name, string Species, double Production, int ProductionDays, int Treatments, decimal Costs)
{
    public double? PerDay => ProductionDays > 0 ? Production / ProductionDays : null;
}
public sealed record YieldLine(string PlantId, string Unit, double Total, int Harvests, string? BestBed, double BestBedTotal);
public sealed record BedRotation(int BedId, string BedName, string ThisYear, IReadOnlyList<string> Crops, string Next, IReadOnlyList<string> Suggestions);

public sealed record SeasonReview(
    int Year, int Plantings, int BedsUsed, IReadOnlyList<CropCount> Crops,
    IReadOnlyDictionary<string, int> Harvests, IReadOnlyDictionary<string, int> Problems, int Waterings,
    decimal Income, decimal Expenses, IReadOnlyList<CategoryTotal> ExpenseByCategory,
    IReadOnlyList<AnimalSeason> Animals, int TasksDone, int TasksLate, int Photos, IReadOnlyList<BedRotation> Rotation, IReadOnlyList<YieldLine> Yields, int HarvestCount)
{
    public decimal Net => Income - Expenses;
}

/// <summary>Classic four-group rotation: legumes, then brassicas, then fruiting crops, then roots and alliums, then legumes again.</summary>
public static class RotationGroups
{
    public static readonly string[] Cycle = { "legume", "brassica", "fruit", "root" };

    public static string? GroupOf(PlantRecord plant)
    {
        if (plant.Category != "vegetable") return null;
        if (plant.Lifecycle is { } l && l.StartsWith("perennial", StringComparison.OrdinalIgnoreCase)) return null;
        return (plant.RotationFamily ?? plant.Family) switch
        {
            "Fabaceae" => "legume", "Brassicaceae" => "brassica", "Solanaceae" or "Cucurbitaceae" => "fruit",
            "Apiaceae" or "Amaryllidaceae" => "root", "Amaranthaceae" or "Asteraceae" => "root", _ => null,
        };
    }

    public static string Next(string group) => Cycle[(Array.IndexOf(Cycle, group) + 1) % Cycle.Length];
}

public static class SeasonReviewBuilder
{
    public static SeasonReview Build(int year, IEnumerable<GardenBed> beds, KnowledgeBase kb, IEnumerable<JournalEntry> journal, IEnumerable<BudgetEntry> budget,
        IEnumerable<AnimalGroup> groups, IEnumerable<GardenTask> tasks, IEnumerable<Photo> photos, DateOnly today, IEnumerable<Harvest>? harvests = null)
    {
        var growing = beds.Where(b => !FeatureCatalog.IsStructure(b.Kind) && b.Kind is not ("path" or "pond" or "compost" or "driveway")).ToList();
        var inYear = growing.SelectMany(b => b.Plantings.Where(p => p.PlantedDate is { } d && d.Year == year).Select(p => (Bed: b, Planting: p))).ToList();

        var crops = inYear.GroupBy(x => x.Planting.PlantId)
            .Select(g => new CropCount(g.Key, g.Sum(x => Math.Max(1, x.Planting.Quantity)), g.Select(x => x.Bed.Name).Distinct().ToList()))
            .OrderByDescending(c => c.Plants).ToList();

        var yearEntries = journal.Where(j => j.Date.Year == year).ToList();
        bool Tagged(JournalEntry j, string tag) => j.Tags.Split(',', StringSplitOptions.TrimEntries).Contains(tag);
        Dictionary<string, int> Count(string tag) => yearEntries.Where(j => Tagged(j, tag) && j.PlantId is not null).GroupBy(j => j.PlantId!).ToDictionary(g => g.Key, g => g.Count());

        var money = budget.Where(e => e.Date.Year == year && e.Amount > 0).ToList();
        var animalLogs = groups.SelectMany(g => g.Logs).Where(l => l.Date.Year == year).ToList();
        var byCat = money.Where(e => !e.IsIncome).GroupBy(e => e.Category).Select(g => new CategoryTotal(g.Key, g.Sum(e => e.Amount))).ToList();
        var animalCost = Enumerable.Range(1, 12).Sum(m => AnimalRules.SpentInMonth(animalLogs, year, m));
        if (animalCost > 0) byCat.Add(new CategoryTotal(BudgetCatalog.AnimalsKey, animalCost));
        var income = money.Where(e => e.IsIncome).Sum(e => e.Amount);

        var animals = groups.Select(g =>
        {
            var logs = g.Logs.Where(l => l.Date.Year == year).ToList();
            var prod = logs.Where(l => l.Kind == "production" && l.Quantity is not null).ToList();
            return new AnimalSeason(g.Id, g.Name, g.Species, prod.Sum(l => l.Quantity!.Value), prod.Select(l => l.Date).Distinct().Count(), logs.Count(l => l.Kind == "treatment"),
                logs.Where(l => l.Kind is "expense" or "treatment" && l.Cost is not null).Sum(l => l.Cost!.Value));
        }).ToList();

        var yearTasks = tasks.Where(t => t.DueDate.Year == year).ToList();

        // rotation: what each bed grew this year (by group) and what comes next
        var rotation = new List<BedRotation>();
        foreach (var bed in inYear.GroupBy(x => x.Bed))
        {
            var plants = bed.Select(x => kb.Get(x.Planting.PlantId)).Where(p => p is not null).Cast<PlantRecord>().ToList();
            var groupsHere = plants.Select(RotationGroups.GroupOf).Where(g => g is not null).GroupBy(g => g!).OrderByDescending(g => g.Count()).ToList();
            if (groupsHere.Count == 0) continue;
            var main = groupsHere[0].Key; var next = RotationGroups.Next(main);
            var suggestions = kb.Plantable.Where(p => RotationGroups.GroupOf(p) == next).Select(p => p.Id).Take(4).ToList();
            rotation.Add(new BedRotation(bed.Key.Id, bed.Key.Name, main, plants.Select(p => p.Id).Distinct().ToList(), next, suggestions));
        }

        var harvestList = (harvests ?? Array.Empty<Harvest>()).Where(h => h.Date.Year == year).ToList();
        var bedNames = beds.ToDictionary(b => b.Id, b => b.Name);
        var yields = harvestList.Where(h => h.Quantity is > 0).GroupBy(h => (h.PlantId, h.Unit)).Select(g =>
        {
            var best = g.Where(h => h.BedId is not null).GroupBy(h => h.BedId!.Value).Select(b => (Bed: b.Key, Total: b.Sum(h => h.Quantity!.Value))).OrderByDescending(b => b.Total).FirstOrDefault();
            return new YieldLine(g.Key.PlantId, g.Key.Unit, g.Sum(h => h.Quantity!.Value), g.Count(), best.Total > 0 && bedNames.TryGetValue(best.Bed, out var n) ? n : null, best.Total);
        }).OrderByDescending(y => y.Total).ToList();
        var harvestCount = harvestList.Count > 0 ? harvestList.Count : Count("harvest").Values.Sum();

        return new SeasonReview(year, inYear.Count, inYear.Select(x => x.Bed.Id).Distinct().Count(), crops, Count("harvest"), Count("pests"),
            yearEntries.Count(j => Tagged(j, "watering")), income, byCat.Sum(c => c.Amount), byCat.OrderByDescending(c => c.Amount).ToList(), animals,
            yearTasks.Count(t => t.Done), yearTasks.Count(t => !t.Done && t.DueDate < today), photos.Count(p => p.Date.Year == year), rotation, yields, harvestCount);
    }

    /// <summary>Years that have anything recorded, newest first (always includes the current one).</summary>
    public static List<int> YearsWithData(IEnumerable<GardenBed> beds, IEnumerable<JournalEntry> journal, IEnumerable<BudgetEntry> budget, IEnumerable<AnimalGroup> groups, int currentYear, IEnumerable<Harvest>? harvests = null)
    {
        var years = new HashSet<int> { currentYear };
        foreach (var d in beds.SelectMany(b => b.Plantings).Select(p => p.PlantedDate).Where(d => d is not null)) years.Add(d!.Value.Year);
        foreach (var j in journal) years.Add(j.Date.Year);
        foreach (var e in budget) years.Add(e.Date.Year);
        foreach (var l in groups.SelectMany(g => g.Logs)) years.Add(l.Date.Year);
        foreach (var h in harvests ?? Array.Empty<Harvest>()) years.Add(h.Date.Year);
        return years.Where(y => y >= 1990 && y <= currentYear + 1).OrderByDescending(y => y).ToList();
    }
}
