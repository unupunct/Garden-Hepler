using System.Diagnostics;
using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Planning;
using GardenHelper.Core.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace GardenHelper.Tests;

/// <summary>A property with years of records: the season review, Today list and look-ahead must stay fast.</summary>
public class StressTests
{
    private readonly ITestOutputHelper _out;
    public StressTests(ITestOutputHelper output) => _out = output;
    private static readonly KnowledgeBase Kb = KnowledgeBase.Load(new[] { Fixtures.RepoContent() });

    [Fact]
    public async Task Years_of_records_load_and_summarise_quickly()
    {
        using var env = await TestEnv.CreateAsync();
        var plantIds = Kb.Plantable.Select(p => p.Id).Take(60).ToArray();
        var today = new DateOnly(2026, 10, 29);
        await using (var db = env.Factory.CreateDbContext())
        {
            db.Gardens.Add(new Garden { Name = "Big", Latitude = 45, Longitude = 25, LastSpringFrost = new DateOnly(2026, 5, 10), FirstAutumnFrost = new DateOnly(2026, 10, 5) });
            await db.SaveChangesAsync();
            var rnd = new Random(1);
            for (var b = 0; b < 600; b++)
            {
                var bed = new GardenBed { GardenId = 1, Name = "Bed " + b, Kind = "raised", X = b * 10, Y = 0 };
                for (var p = 0; p < 6; p++) bed.Plantings.Add(new BedPlanting { PlantId = plantIds[rnd.Next(plantIds.Length)], Quantity = 5, PlantedDate = today.AddDays(-rnd.Next(1500)), Status = PlantingStatus.Growing });
                db.Beds.Add(bed);
            }
            for (var i = 0; i < 6000; i++) db.Journal.Add(new JournalEntry { Date = today.AddDays(-rnd.Next(1500)), Title = "Entry " + i, Text = "x", Tags = i % 3 == 0 ? "harvest" : "watering", PlantId = plantIds[rnd.Next(plantIds.Length)] });
            for (var i = 0; i < 4000; i++) db.Harvests.Add(new Harvest { Date = today.AddDays(-rnd.Next(1500)), PlantId = plantIds[rnd.Next(plantIds.Length)], BedId = 1 + rnd.Next(600), Quantity = rnd.Next(1, 30), Unit = "kg" });
            for (var i = 0; i < 3000; i++) db.Photos.Add(new Photo { Date = today.AddDays(-rnd.Next(1500)), File = "x.jpg", Caption = "p" + i });
            for (var g = 0; g < 20; g++)
            {
                var grp = new AnimalGroup { GardenId = 1, Name = "Group " + g, Species = "chicken", Count = 30 };
                grp.Rations.Add(new FeedRation { Name = "Feed", GramsPerAnimalPerDay = 100, PricePerKg = 2 });
                for (var i = 0; i < 700; i++) grp.Logs.Add(new AnimalLog { Date = today.AddDays(-rnd.Next(1500)), Kind = "production", Quantity = rnd.Next(10, 30) });
                db.AnimalGroups.Add(grp);
            }
            for (var i = 0; i < 3000; i++) db.Tasks.Add(new GardenTask { Title = "Task " + i, DueDate = today.AddDays(-rnd.Next(1500)), Done = i % 5 != 0 });
            await db.SaveChangesAsync();
        }

        var repo = new Repository(env.Factory);
        var sw = Stopwatch.StartNew();
        var beds = await repo.ListAsync<GardenBed>(q => q.Include(b => b.Plantings));
        var journal = await repo.ListAsync<JournalEntry>(null); var harvests = await repo.ListAsync<Harvest>(null); var photos = await repo.ListAsync<Photo>(null);
        var groups = await repo.ListAsync<AnimalGroup>(q => q.Include(g => g.Logs).Include(g => g.Rations)); var tasks = await repo.ListAsync<GardenTask>(null);
        var load = sw.ElapsedMilliseconds; sw.Restart();
        var review = SeasonReviewBuilder.Build(2026, beds, Kb, journal, new List<BudgetEntry>(), groups, tasks, photos, today, harvests);
        var build = sw.ElapsedMilliseconds; sw.Restart();
        var items = TodayPlanner.Build(today, tasks.Where(t => !t.Done && t.DueDate <= today), new List<InventoryPlant>(), groups);
        var planner = sw.ElapsedMilliseconds; sw.Restart();
        var crops = plantIds.Select(Kb.Get).Where(p => p is not null).Cast<PlantRecord>();
        var ahead = LookAhead.Build(today, await repo.ActiveGardenAsync(), crops, new List<InventoryPlant>(), tasks, p => p.Id, a => a.ToString(), System.Globalization.CultureInfo.InvariantCulture);
        var look = sw.ElapsedMilliseconds; sw.Restart();
        var ripening = HarvestForecast.Upcoming(beds, Kb, today);
        var ripe = sw.ElapsedMilliseconds;
        _out.WriteLine($"load {load} ms, season {build} ms, today {planner} ms, look-ahead {look} ms, ripening {ripe} ms; beds {beds.Count}, journal {journal.Count}, review plantings {review.Plantings}");
        Assert.True(load < 15000, "loading took " + load + " ms");
        Assert.True(build < 2000, "season review took " + build + " ms");
        Assert.True(planner < 1000 && look < 2000 && ripe < 1000, $"planner {planner}, look-ahead {look}, ripening {ripe}");
        Assert.NotEmpty(items); Assert.NotNull(ahead);
    }
}
