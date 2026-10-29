using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Greenhouse;
using GardenHelper.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace GardenHelper.Core.Planning;

public sealed record TaskCounts(int Overdue, int Today, int Week);
public sealed record AnimalBrief(int Id, string Name, string Species, int Count, double? AveragePerDay, double EggsToday, string? WithdrawalUntil);
public sealed record GreenhouseBrief(int Id, string Name, double? AirTemp, double? Humidity, double? SoilMoisture, int Alerts, int? MinutesSinceData, Dictionary<string, bool> Switches);
public sealed record TaskBrief(int Id, string Title, string Due, bool Overdue);
public sealed record UpcomingBrief(string Date, string Kind, string Title);
public sealed record CameraBrief(int Id, string Name, string Kind);
public sealed record MoneyBrief(decimal Income, decimal Expenses, decimal Balance);
public sealed record Summary(string Date, TaskCounts Tasks, int WateringDue, int AnimalsTotal, double EggsToday, bool WithdrawalActive, MoneyBrief Month,
                             IReadOnlyList<AnimalBrief> Animals, IReadOnlyList<GreenhouseBrief> Greenhouses, IReadOnlyList<UpcomingBrief> Upcoming);

/// <summary>What the phone page and Home Assistant read: a compact picture of the property, plus the few actions they may take.</summary>
public sealed class RemoteService
{
    private readonly Repository _repo;
    private readonly GreenhouseService _greenhouse;
    public RemoteService(Repository repo, GreenhouseService greenhouse) { _repo = repo; _greenhouse = greenhouse; }

    public async Task<Summary> SummaryAsync(DateOnly today, DateTime nowUtc, CancellationToken ct = default)
    {
        var tasks = await _repo.ListAsync<GardenTask>(q => q.Where(t => !t.Done), ct);
        var groups = await _repo.ListAsync<AnimalGroup>(q => q.Include(g => g.Logs).Include(g => g.Rations).OrderBy(g => g.Name), ct);
        var inventory = await _repo.ListAsync<InventoryPlant>(q => q.Where(p => p.Status != "dead"), ct);
        var entries = await _repo.ListAsync<BudgetEntry>(q => q.Where(e => e.Date.Year == today.Year && e.Date.Month == today.Month), ct);
        var houses = await _repo.ListAsync<PropertyFeature>(q => q.Where(f => f.Kind == "greenhouse").OrderBy(f => f.Name), ct);

        var counts = new TaskCounts(tasks.Count(t => t.DueDate < today), tasks.Count(t => t.DueDate == today), tasks.Count(t => t.DueDate > today && t.DueDate <= today.AddDays(7)));
        var watering = inventory.Count(p => InventoryRules.DaysOverdue(p, today) is >= 0);

        var animals = groups.Select(g =>
        {
            var s = AnimalRules.Summarize(g, today);
            var eggs = g.Logs.Where(l => l.Kind == "production" && l.Date == today).Sum(l => l.Quantity ?? 0);
            return new AnimalBrief(g.Id, g.Name, g.Species, g.Count, s.AveragePerDay, eggs, s.WithdrawalUntil?.ToString("yyyy-MM-dd"));
        }).ToList();

        var ghs = new List<GreenhouseBrief>();
        foreach (var f in houses)
        {
            var setup = await _greenhouse.SetupAsync(f.Id, ct);
            var latest = await _greenhouse.LatestAsync(f.Id, ct);
            var values = latest.ToDictionary(kv => kv.Key, kv => kv.Value.Value);
            DateTime? last = latest.Count == 0 ? null : latest.Values.Max(v => v.AtUtc);
            var alerts = GreenhouseRules.Evaluate(setup, values, last, nowUtc).Count(a => a.Severity != "info");
            double? V(string k) => values.TryGetValue(k, out var x) ? x : null;
            ghs.Add(new GreenhouseBrief(f.Id, f.Name, V("air_temp"), V("humidity"), V("soil_moisture"), alerts,
                last is null ? null : (int)Math.Round((nowUtc - last.Value).TotalMinutes), await _greenhouse.CommandsAsync(f.Id, ct)));
        }

        var treatments = groups.SelectMany(g => g.Logs.Where(l => l.Kind == "treatment").Select(l => (l, g.Name)));
        var upcoming = ComingUpPlanner.Build(tasks, treatments, today, 14).Take(12)
            .Select(i => new UpcomingBrief(i.Date.ToString("yyyy-MM-dd"), i.Kind, i.Title)).ToList();

        var money = BudgetRules.Summarize(entries, groups.SelectMany(g => g.Logs), today.Year, today.Month);
        return new Summary(today.ToString("yyyy-MM-dd"), counts, watering, groups.Sum(g => g.Count), animals.Sum(a => a.EggsToday),
            animals.Any(a => a.WithdrawalUntil is not null), new MoneyBrief(money.Income, money.Expenses, money.Net), animals, ghs, upcoming);
    }

    public async Task<List<TaskBrief>> OpenTasksAsync(DateOnly today, int max = 60, CancellationToken ct = default) =>
        (await _repo.ListAsync<GardenTask>(q => q.Where(t => !t.Done).OrderBy(t => t.DueDate).Take(max), ct))
            .Select(t => new TaskBrief(t.Id, t.Title, t.DueDate.ToString("yyyy-MM-dd"), t.DueDate < today)).ToList();

    public async Task<bool> CompleteTaskAsync(int id, CancellationToken ct = default)
    {
        var t = (await _repo.ListAsync<GardenTask>(q => q.Where(x => x.Id == id), ct)).FirstOrDefault();
        if (t is null) return false;
        if (!t.Done) { t.Done = true; t.CompletedUtc = DateTime.UtcNow; await _repo.SaveAsync(t, ct); }
        return true;
    }

    /// <summary>Adds eggs or milk for today to an animal group. Returns false for an unknown group or a quantity that is not 0.1-10000.</summary>
    public async Task<bool> AddProductionAsync(int groupId, double quantity, DateOnly today, CancellationToken ct = default)
    {
        if (!double.IsFinite(quantity) || quantity < 0.1 || quantity > 10000) return false;
        if ((await _repo.CountAsync<AnimalGroup>(q => q.Where(g => g.Id == groupId), ct)) == 0) return false;
        await _repo.SaveAsync(new AnimalLog { GroupId = groupId, Date = today, Kind = "production", Quantity = quantity, Text = "phone" }, ct);
        return true;
    }

    public async Task<List<CameraBrief>> CamerasAsync(CancellationToken ct = default) =>
        (await _repo.ListAsync<Cameras.Camera>(q => q.Where(c => c.Enabled).OrderBy(c => c.Name), ct)).Select(c => new CameraBrief(c.Id, c.Name, c.Kind)).ToList();
}
