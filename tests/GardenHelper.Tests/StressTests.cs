using System.Diagnostics;
using GardenHelper.Core.Animals;
using GardenHelper.Core.Data;
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
            BulkData.Add(db, 1, plantIds, today);
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
