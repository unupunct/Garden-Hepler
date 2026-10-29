using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Planning;
using Xunit;

namespace GardenHelper.Tests;

public class SeasonReviewTests
{
    private static readonly KnowledgeBase Kb = KnowledgeBase.Load(new[] { Fixtures.RepoContent() });
    private static readonly DateOnly Today = new(2026, 10, 29);

    [Fact]
    public void Review_adds_up_plantings_money_animals_and_tasks_for_the_year()
    {
        var bed = new GardenBed { Id = 1, Name = "Raised bed 1", Kind = "raised", Plantings = new()
        {
            new BedPlanting { Id = 1, PlantId = "tomato", Quantity = 6, PlantedDate = new DateOnly(2026, 5, 20) },
            new BedPlanting { Id = 2, PlantId = "pepper", Quantity = 4, PlantedDate = new DateOnly(2026, 5, 20) },
            new BedPlanting { Id = 3, PlantId = "carrot", Quantity = 30, PlantedDate = new DateOnly(2025, 4, 10) },
        } };
        var journal = new[]
        {
            new JournalEntry { Date = new DateOnly(2026, 8, 1), PlantId = "tomato", Tags = "harvest" }, new JournalEntry { Date = new DateOnly(2026, 8, 5), PlantId = "tomato", Tags = "harvest" },
            new JournalEntry { Date = new DateOnly(2026, 9, 1), PlantId = "tomato", Tags = "pests" }, new JournalEntry { Date = new DateOnly(2026, 7, 1), PlantId = "tomato", Tags = "watering" },
            new JournalEntry { Date = new DateOnly(2025, 7, 1), PlantId = "carrot", Tags = "harvest" },
        };
        var budget = new[] { new BudgetEntry { Date = new DateOnly(2026, 3, 1), Amount = 100, Category = "seeds" }, new BudgetEntry { Date = new DateOnly(2026, 6, 1), Amount = 250, IsIncome = true, Category = "sales" } };
        var hens = new AnimalGroup { Id = 5, Name = "Hens", Species = "chicken", Logs = new()
        {
            new AnimalLog { Date = new DateOnly(2026, 6, 1), Kind = "production", Quantity = 20 }, new AnimalLog { Date = new DateOnly(2026, 6, 2), Kind = "production", Quantity = 22 },
            new AnimalLog { Date = new DateOnly(2026, 6, 3), Kind = "treatment", Cost = 15 },
        } };
        var tasks = new[] { new GardenTask { DueDate = new DateOnly(2026, 6, 1), Done = true }, new GardenTask { DueDate = new DateOnly(2026, 10, 1) } };

        var r = SeasonReviewBuilder.Build(2026, new[] { bed }, Kb, journal, budget, new[] { hens }, tasks, Array.Empty<Photo>(), Today);

        Assert.Equal(2, r.Plantings);                                  // the carrot is last year's
        Assert.Equal(1, r.BedsUsed);
        Assert.Equal(2, r.Harvests["tomato"]); Assert.Equal(1, r.Problems["tomato"]); Assert.Equal(1, r.Waterings);
        Assert.Equal(250, r.Income); Assert.Equal(115, r.Expenses); Assert.Equal(135, r.Net);
        var a = Assert.Single(r.Animals); Assert.Equal(42, a.Production); Assert.Equal(2, a.ProductionDays); Assert.Equal(21, a.PerDay); Assert.Equal(1, a.Treatments);
        Assert.Equal(1, r.TasksDone); Assert.Equal(1, r.TasksLate);
        var rot = Assert.Single(r.Rotation); Assert.Equal("fruit", rot.ThisYear); Assert.Equal("root", rot.Next); Assert.NotEmpty(rot.Suggestions);
    }

    [Fact]
    public void Yields_add_up_per_crop_and_name_the_best_bed()
    {
        var b1 = new GardenBed { Id = 1, Name = "Bed A", Kind = "raised" }; var b2 = new GardenBed { Id = 2, Name = "Bed B", Kind = "raised" };
        var harvests = new[]
        {
            new Harvest { Date = new DateOnly(2026, 8, 1), PlantId = "tomato", BedId = 1, Quantity = 5, Unit = "kg" }, new Harvest { Date = new DateOnly(2026, 8, 8), PlantId = "tomato", BedId = 2, Quantity = 9, Unit = "kg" },
            new Harvest { Date = new DateOnly(2026, 8, 9), PlantId = "tomato", BedId = 2, Quantity = 4, Unit = "kg" }, new Harvest { Date = new DateOnly(2026, 8, 9), PlantId = "tomato", BedId = 1 },
            new Harvest { Date = new DateOnly(2025, 8, 9), PlantId = "tomato", BedId = 1, Quantity = 50, Unit = "kg" },
        };
        var r = SeasonReviewBuilder.Build(2026, new[] { b1, b2 }, Kb, Array.Empty<JournalEntry>(), Array.Empty<BudgetEntry>(), Array.Empty<AnimalGroup>(), Array.Empty<GardenTask>(), Array.Empty<Photo>(), new DateOnly(2026, 10, 29), harvests);
        var y = Assert.Single(r.Yields);
        Assert.Equal(18, y.Total); Assert.Equal(3, y.Harvests); Assert.Equal("Bed B", y.BestBed); Assert.Equal(13, y.BestBedTotal); Assert.Equal(4, r.HarvestCount);
    }

    [Fact]
    public void Rotation_cycle_goes_legume_brassica_fruit_root_and_back()
    {
        Assert.Equal("brassica", RotationGroups.Next("legume")); Assert.Equal("legume", RotationGroups.Next("root"));
    }

    [Fact]
    public void Years_with_data_always_include_the_current_year_newest_first()
    {
        var years = SeasonReviewBuilder.YearsWithData(Array.Empty<GardenBed>(), new[] { new JournalEntry { Date = new DateOnly(2024, 1, 1) } }, Array.Empty<BudgetEntry>(), Array.Empty<AnimalGroup>(), 2026);
        Assert.Equal(new[] { 2026, 2024 }, years);
    }
}

public class PlantingCalendarTests
{
    private static readonly GardenHelper.Core.Knowledge.KnowledgeBase Kb = GardenHelper.Core.Knowledge.KnowledgeBase.Load(new[] { Fixtures.RepoContent() });

    [Fact]
    public void Tomato_has_indoor_sowing_planting_out_and_one_harvest_stretch()
    {
        var garden = new GardenHelper.Core.Domain.Garden { LastSpringFrost = new DateOnly(2026, 5, 10), FirstAutumnFrost = new DateOnly(2026, 10, 5) };
        var rows = GardenHelper.Core.Planning.PlantingCalendar.Build(new[] { Kb.Get("tomato")! }, garden, 2026);
        var row = Assert.Single(rows);
        Assert.Contains(row.Bars, b => b.Kind == "sow_indoors");
        Assert.Single(row.Bars, b => b.Kind == "harvest");
        Assert.All(row.Bars, b => Assert.InRange(b.StartDay, 1, 366));
        var harvest = row.Bars.Single(b => b.Kind == "harvest"); var indoor = row.Bars.First(b => b.Kind == "sow_indoors");
        Assert.True(harvest.StartDay > indoor.StartDay);
    }
}
