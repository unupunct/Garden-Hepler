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
    public void Costs_per_kilogram_and_per_egg_are_worked_out_from_inputs_feed_and_production()
    {
        var bed = new GardenBed { Id = 1, Name = "A", Kind = "raised" };
        var hens = new AnimalGroup { Id = 3, Name = "Hens", Species = "chicken", Count = 10, Rations = new() { new FeedRation { GramsPerAnimalPerDay = 100, PricePerKg = 2 } }, Logs = new()
            { new AnimalLog { Date = new DateOnly(2026, 3, 1), Kind = "production", Quantity = 600 }, new AnimalLog { Date = new DateOnly(2026, 3, 2), Kind = "expense", Cost = 50 } } };
        var budget = new[] { new BudgetEntry { Date = new DateOnly(2026, 2, 1), Amount = 80, Category = "seeds" }, new BudgetEntry { Date = new DateOnly(2026, 2, 2), Amount = 20, Category = "plants" }, new BudgetEntry { Date = new DateOnly(2026, 6, 1), Amount = 900, Category = "infrastructure" } };
        var harvests = new[] { new Harvest { Date = new DateOnly(2026, 8, 1), PlantId = "tomato", BedId = 1, Quantity = 40, Unit = "kg" } };
        var r = SeasonReviewBuilder.Build(2026, new[] { bed }, Kb, Array.Empty<JournalEntry>(), budget, new[] { hens }, Array.Empty<GardenTask>(), Array.Empty<Photo>(), new DateOnly(2026, 12, 31), harvests);
        Assert.Equal(100, r.InputsCost); Assert.Equal(40, r.KgHarvested);
        var e = Assert.Single(r.Economy);
        Assert.Equal(10 * 0.1m * 2 * 365, e.FeedCost);                    // 1 kg a day at 2 per kg for the 365 days counted
        Assert.Equal(50, e.OtherCosts); Assert.Equal((730m + 50m) / 600m, e.CostPerUnit);
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

public class LookAheadTests
{
    private static readonly GardenHelper.Core.Knowledge.KnowledgeBase Kb = GardenHelper.Core.Knowledge.KnowledgeBase.Load(new[] { Fixtures.RepoContent() });
    private static readonly System.Globalization.CultureInfo C = System.Globalization.CultureInfo.InvariantCulture;
    private static List<GardenHelper.Core.Planning.TodayItem> Run(DateOnly today, GardenHelper.Core.Domain.Garden g, IEnumerable<GardenHelper.Core.Knowledge.PlantRecord> crops, IEnumerable<GardenHelper.Core.Domain.InventoryPlant> trees, IEnumerable<GardenHelper.Core.Domain.GardenTask>? tasks = null) =>
        GardenHelper.Core.Planning.LookAhead.Build(today, g, crops, trees, tasks ?? Array.Empty<GardenHelper.Core.Domain.GardenTask>(), p => p.Id, a => a.ToString(), C);

    [Fact]
    public void A_sowing_window_opening_this_week_is_suggested_once_and_not_when_already_planned()
    {
        var garden = new GardenHelper.Core.Domain.Garden { LastSpringFrost = new DateOnly(2026, 5, 10), FirstAutumnFrost = new DateOnly(2026, 10, 5) };
        var tomato = Kb.Get("tomato")!;
        var plan = GardenHelper.Core.Knowledge.PlantingCalculator.Calculate(tomato, garden, 2026).Windows.First(w => w.Activity == GardenHelper.Core.Knowledge.PlantingActivity.SowIndoors);
        var day = plan.Start.AddDays(-3);
        var items = Run(day, garden, new[] { tomato }, Array.Empty<GardenHelper.Core.Domain.InventoryPlant>());
        var one = Assert.Single(items, i => i.Key == "look_sow_opens"); Assert.Equal("tomato", one.PlantId); Assert.Equal(plan.Start, one.Due);
        var planned = Run(day, garden, new[] { tomato }, Array.Empty<GardenHelper.Core.Domain.InventoryPlant>(), new[] { new GardenHelper.Core.Domain.GardenTask { PlantId = "tomato", DueDate = plan.Start } });
        Assert.DoesNotContain(planned, i => i.Key == "look_sow_opens");
    }

    [Fact]
    public void Fruit_trees_get_seasonal_care_and_winter_prep_follows_the_first_frost_date()
    {
        var garden = new GardenHelper.Core.Domain.Garden { FirstAutumnFrost = new DateOnly(2026, 10, 5) };
        var trees = new[] { new GardenHelper.Core.Domain.InventoryPlant { IsTree = true, TreeType = "fruit", SpeciesId = "plum", Name = "Plum" } };
        var apple = new[] { new GardenHelper.Core.Domain.InventoryPlant { IsTree = true, TreeType = "fruit", SpeciesId = "apple", Name = "Apple" } };
        Assert.Contains(Run(new DateOnly(2026, 1, 20), garden, Array.Empty<GardenHelper.Core.Knowledge.PlantRecord>(), apple), i => i.Key == "look_tree_prune_winter");
        Assert.DoesNotContain(Run(new DateOnly(2026, 1, 20), garden, Array.Empty<GardenHelper.Core.Knowledge.PlantRecord>(), trees), i => i.Key == "look_tree_prune_winter");   // plum: summer pruning only
        Assert.Contains(Run(new DateOnly(2026, 8, 10), garden, Array.Empty<GardenHelper.Core.Knowledge.PlantRecord>(), trees), i => i.Key == "look_tree_summer_prune");
        Assert.Contains(Run(new DateOnly(2026, 9, 25), garden, Array.Empty<GardenHelper.Core.Knowledge.PlantRecord>(), trees), i => i.Key == "look_winter_protect");
        Assert.DoesNotContain(Run(new DateOnly(2026, 7, 1), garden, Array.Empty<GardenHelper.Core.Knowledge.PlantRecord>(), trees), i => i.Key.StartsWith("look_winter"));
    }

    [Fact]
    public void Tree_care_windows_move_with_the_frost_dates()
    {
        var apple = new[] { new GardenHelper.Core.Domain.InventoryPlant { IsTree = true, TreeType = "fruit", SpeciesId = "apple", Name = "Apple" } };
        var none = Array.Empty<GardenHelper.Core.Knowledge.PlantRecord>();
        var cold = new GardenHelper.Core.Domain.Garden { LastSpringFrost = new DateOnly(2026, 5, 25), FirstAutumnFrost = new DateOnly(2026, 9, 25) };
        var warm = new GardenHelper.Core.Domain.Garden { LastSpringFrost = new DateOnly(2026, 4, 1), FirstAutumnFrost = new DateOnly(2026, 11, 10) };
        var day = new DateOnly(2026, 3, 25);                         // two months before the cold garden's last frost, a few days before the warm one's
        Assert.Contains(Run(day, cold, none, apple), i => i.Key == "look_tree_prune_winter");
        Assert.DoesNotContain(Run(day, warm, none, apple), i => i.Key == "look_tree_prune_winter");
        Assert.Contains(Run(new DateOnly(2026, 10, 15), cold, none, apple), i => i.Key == "look_tree_whitewash");
        Assert.DoesNotContain(Run(new DateOnly(2026, 10, 15), warm, none, apple), i => i.Key == "look_tree_whitewash");
    }
}

public class AnimalCareTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);

    [Fact]
    public void Feed_days_left_count_down_from_the_day_the_stock_was_set()
    {
        var r = new GardenHelper.Core.Animals.FeedRation { GramsPerAnimalPerDay = 1500, StockKg = 30, StockDate = Today.AddDays(-1) };
        Assert.Equal(4, GardenHelper.Core.Animals.AnimalRules.FeedDaysLeft(r, 4, Today)!.Value, 1);            // 4 goats eat 6 kg a day: 24 kg left = 4 days
        Assert.Null(GardenHelper.Core.Animals.AnimalRules.FeedDaysLeft(new GardenHelper.Core.Animals.FeedRation { GramsPerAnimalPerDay = 100 }, 4, Today));
    }

    [Fact]
    public void Care_due_soon_and_low_feed_show_up_for_today_but_far_off_things_do_not()
    {
        var g = new GardenHelper.Core.Animals.AnimalGroup { Id = 1, Name = "Goats", Species = "goat", Count = 4,
            Rations = new() { new GardenHelper.Core.Animals.FeedRation { Name = "Hay", GramsPerAnimalPerDay = 1500, StockKg = 30, StockDate = Today.AddDays(-1) } },
            Logs = new()
            {
                new GardenHelper.Core.Animals.AnimalLog { Kind = "vaccination", Date = Today.AddDays(-80), DueDate = Today.AddDays(3) },
                new GardenHelper.Core.Animals.AnimalLog { Kind = "worming", Date = Today.AddDays(-20), DueDate = Today.AddDays(60) },
                new GardenHelper.Core.Animals.AnimalLog { Kind = "weight", Date = Today.AddDays(-2), Quantity = 55 },
            } };
        var items = GardenHelper.Core.Planning.TodayPlanner.Build(Today, Array.Empty<GardenHelper.Core.Domain.GardenTask>(), Array.Empty<GardenHelper.Core.Domain.InventoryPlant>(), new[] { g });
        Assert.Contains(items, i => i.Key == "today_animal_vaccination");
        Assert.DoesNotContain(items, i => i.Key == "today_animal_worming");
        Assert.Contains(items, i => i.Key == "today_feed_low" && i.Severity == "soon");
        Assert.Equal(55, GardenHelper.Core.Animals.AnimalRules.LastWeight(g.Logs)!.Value.Kg);
        Assert.Equal(150, GardenHelper.Core.Animals.AnimalRules.Gestation("goat"));
    }
}
