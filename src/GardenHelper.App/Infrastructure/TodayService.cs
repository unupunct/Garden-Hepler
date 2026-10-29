using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Greenhouse;
using GardenHelper.Core.Planning;
using GardenHelper.Core.Services;
using GardenHelper.Core.Site;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.Infrastructure;

/// <summary>Everything that needs the owner today: tasks, plants, animals, the forecast, garden advice and greenhouse alerts. Used by the Today page and by reminders.</summary>
public sealed class TodayService
{
    public const string WeatherKey = "weather_cache";
    private readonly Repository _repo;
    private readonly AlertService _alerts;
    private readonly GreenhouseService _greenhouse;
    private readonly SettingsService _settings;
    private readonly ILogger<TodayService> _log;

    public TodayService(Repository repo, AlertService alerts, GreenhouseService greenhouse, SettingsService settings, ILogger<TodayService> log)
    { _repo = repo; _alerts = alerts; _greenhouse = greenhouse; _settings = settings; _log = log; }

    public async Task<WeatherForecast?> CachedForecastAsync() => WeatherForecast.FromJson(await _settings.GetAsync(WeatherKey));

    public async Task<List<TodayItem>> BuildAsync(Garden? garden, WeatherForecast? forecast)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var tasks = await _repo.ListAsync<GardenTask>(q => q.Where(t => !t.Done && t.DueDate <= today));
        var plants = await _repo.ListAsync<InventoryPlant>(q => q.Where(p => !p.IsTree));
        var groups = await _repo.ListAsync<AnimalGroup>(q => q.Include(g => g.Logs).Include(g => g.Rations));
        var items = TodayPlanner.Build(today, tasks, plants, groups, "d MMM");

        if (forecast is not null) items.AddRange(WeatherAdvice.Evaluate(forecast.Days, today, Loc.Instance.Culture));
        foreach (var row in await _alerts.LoadAsync())
            if (row.Alert.Severity is AlertSeverity.Urgent or AlertSeverity.Soon)
                items.Add(new TodayItem("advice", row.Alert.Severity == AlertSeverity.Urgent ? "urgent" : "soon", row.Glyph, "today_advice", new[] { row.Message }, TodayAction.OpenActions, row.Alert.PlantingId ?? 0));
        if (garden is not null)
            foreach (var f in await _repo.ListAsync<PropertyFeature>(q => q.Where(x => x.GardenId == garden.Id && x.Kind == "greenhouse")))
            {
                try
                {
                    var setup = await _greenhouse.SetupAsync(f.Id); var latest = await _greenhouse.LatestAsync(f.Id);
                    DateTime? last = latest.Count == 0 ? null : latest.Values.Max(v => v.AtUtc);
                    foreach (var a in GreenhouseRules.Evaluate(setup, latest.ToDictionary(kv => kv.Key, kv => kv.Value.Value), last, DateTime.UtcNow).Where(a => a.Severity is "critical" or "warning"))
                        items.Add(new TodayItem("greenhouse", a.Severity == "critical" ? "urgent" : "soon", a.Severity == "critical" ? "⛔" : "⚠", "today_greenhouse", new[] { f.Name, Loc.F(a.Key, a.Args) }, TodayAction.OpenGreenhouse, f.Id));
                }
                catch (Exception ex) { _log.LogWarning(ex, "Greenhouse alerts failed"); }
            }
        return items.OrderBy(i => TodayPlanner.SeverityRank(i.Severity)).ThenBy(i => Array.IndexOf(TodayPlanner.Sections, i.Section)).ToList();
    }

    public static string TitleOf(TodayItem i) => Loc.F(i.Key, i.Args.Cast<object>().ToArray());
}
