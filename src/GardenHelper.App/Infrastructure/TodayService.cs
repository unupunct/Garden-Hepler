using GardenHelper.Core.Animals;
using GardenHelper.Core.Knowledge;
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
    private readonly KnowledgeBase _kb;

    public TodayService(Repository repo, AlertService alerts, GreenhouseService greenhouse, SettingsService settings, KnowledgeBase kb, ILogger<TodayService> log)
    { _kb = kb; _repo = repo; _alerts = alerts; _greenhouse = greenhouse; _settings = settings; _log = log; }

    public async Task<WeatherForecast?> CachedForecastAsync() => WeatherForecast.FromJson(await _settings.GetAsync(WeatherKey));

    public async Task<List<TodayItem>> BuildAsync(Garden? garden, WeatherForecast? forecast)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var tasks = await _repo.ListAsync<GardenTask>(q => q.Where(t => !t.Done && t.DueDate <= today));
        var plants = await _repo.ListAsync<InventoryPlant>(q => q.Where(p => !p.IsTree));
        var groups = await _repo.ListAsync<AnimalGroup>(q => q.Include(g => g.Logs).Include(g => g.Rations));
        var items = TodayPlanner.Build(today, tasks, plants, groups, "d MMM");

        if (forecast is not null) items.AddRange(WeatherAdvice.Evaluate(forecast.Days, today, Loc.Instance.Culture));
        if (garden is not null) items.AddRange(await BedWateringAsync(garden, forecast, today));
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

    /// <summary>Beds that need water today: from the soil sensor when there is one, otherwise from hot weather and the last watering; rain that is coming cancels it.</summary>
    private async Task<List<TodayItem>> BedWateringAsync(Garden garden, WeatherForecast? forecast, DateOnly today)
    {
        try
        {
            var beds = (await _repo.ListAsync<GardenBed>(q => q.Where(b => b.GardenId == garden.Id).Include(b => b.Plantings)))
                .Where(b => !FeatureCatalog.IsStructure(b.Kind) && b.Kind is not ("path" or "pond" or "compost" or "driveway")).ToList();
            var watered = await _repo.ListAsync<JournalEntry>(q => q.Where(j => j.Tags.Contains("watering")));
            var cache = new Dictionary<int, Dictionary<string, (double Value, DateTime AtUtc)>>();
            var rows = new List<BedWatering>();
            foreach (var b in beds)
            {
                var active = b.Plantings.Where(p => p.Status is PlantingStatus.Sown or PlantingStatus.Growing or PlantingStatus.Harvesting).Select(p => p.PlantId).Distinct().ToList();
                if (active.Count == 0) continue;
                DateOnly? last = watered.Where(j => j.PlantId is not null && active.Contains(j.PlantId)).Select(j => (DateOnly?)j.Date).Max();
                double? moisture = null;
                if ((b.SensorFeatureId ?? b.FeatureId) is { } fid)
                {
                    if (!cache.TryGetValue(fid, out var latest)) cache[fid] = latest = await _greenhouse.LatestAsync(fid);
                    if (latest.TryGetValue("soil_moisture", out var m)) moisture = m.Value;
                }
                rows.Add(new BedWatering(b.Id, b.Name, moisture, last, true));
            }
            return WateringAdvisor.Evaluate(rows, forecast?.Days ?? new List<WeatherDay>(), today, Loc.Instance.Culture);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Bed watering check failed"); return new(); }
    }

    /// <summary>Does what the button says for the actions that change records (task done, plant or bed watered, eggs or milk logged). Returns false when it could not.</summary>
    public async Task<bool> PerformAsync(TodayAction action, int refId, int[]? refIds, double? quantity)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        switch (action)
        {
            case TodayAction.Done:
                if ((await _repo.ListAsync<GardenTask>(q => q.Where(t => t.Id == refId))).FirstOrDefault() is not { } t) return false;
                t.Done = true; t.CompletedUtc = DateTime.UtcNow; await _repo.SaveAsync(t); return true;
            case TodayAction.Watered:
                if ((await _repo.ListAsync<InventoryPlant>(q => q.Where(p => p.Id == refId))).FirstOrDefault() is not { } plant) return false;
                plant.LastWatered = today; await _repo.SaveAsync(plant);
                await _repo.SaveAsync(new InventoryLog { PlantId = plant.Id, Date = today, Kind = "watering", Text = "" }); return true;
            case TodayAction.WaterBed:
                var ids = refIds ?? new[] { refId };
                foreach (var bed in await _repo.ListAsync<GardenBed>(q => q.Where(b => ids.Contains(b.Id)).Include(b => b.Plantings)))
                    foreach (var plantId in bed.Plantings.Where(x => x.Status is PlantingStatus.Sown or PlantingStatus.Growing or PlantingStatus.Harvesting).Select(x => x.PlantId).Distinct())
                        await _repo.SaveAsync(new JournalEntry { Title = Loc.F("card_water_title", _kb.Get(plantId)?.DisplayName(Loc.Instance.Language) ?? plantId, bed.Name), Text = "", Tags = "watering", PlantId = plantId });
                return true;
            case TodayAction.LogProduction:
                if (quantity is not { } q || q < 0 || q > 100000) return false;
                await _repo.SaveAsync(new AnimalLog { GroupId = refId, Date = today, Kind = "production", Quantity = q, Text = "" }); return true;
            default: return false;
        }
    }

    public static string TitleOf(TodayItem i) => Loc.F(i.Key, i.Args.Cast<object>().ToArray());
}
