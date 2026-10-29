using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Site;

namespace GardenHelper.Core.Services;

public enum AlertSeverity { Urgent = 0, Soon = 1, Info = 2 }

/// <summary>
/// A "what to do next" suggestion. <see cref="Key"/> selects the localized sentence; <see cref="Args"/> fill it.
/// <see cref="Why"/> names the data the rule used so the user can judge it.
/// </summary>
public sealed record GardenAlert(AlertSeverity Severity, string Key, string[] Args, DateOnly? Due, int? PlantingId, string? PlantId, string Why);

/// <summary>Transparent rules that turn plantings, frost dates, sun and climate into next actions.</summary>
public static class ActionAdvisor
{
    public static List<GardenAlert> Evaluate(Garden? garden, IReadOnlyList<GardenBed> beds, KnowledgeBase kb,
        IReadOnlyList<SeedItem> seeds, DateOnly today, ClimateSummary? climate = null, string lang = "en")
    {
        var alerts = new List<GardenAlert>();
        foreach (var bed in beds)
        {
            var isGrowingSpace = bed.Kind is "bed" or "raised" or "greenhouse";
            var active = bed.Plantings.Where(p => p.Status != PlantingStatus.Finished).ToList();
            if (isGrowingSpace && active.Count == 0)
                alerts.Add(new(AlertSeverity.Info, "alert_bed_empty", new[] { bed.Name }, null, null, null, "bed has no active plantings"));

            foreach (var p in active)
            {
                var plant = kb.Get(p.PlantId);
                if (plant is null) continue;
                var name = plant.DisplayName(lang);
                CheckSun(alerts, bed, p, plant, name, garden, today);
                switch (p.Status)
                {
                    case PlantingStatus.Planned: CheckPlanned(alerts, bed, p, plant, name, garden, seeds, today); break;
                    case PlantingStatus.Sown or PlantingStatus.Growing or PlantingStatus.Harvesting: CheckGrowing(alerts, bed, p, plant, name, garden, today); break;
                }
            }
        }
        return alerts.OrderBy(a => a.Severity).ThenBy(a => a.Due ?? DateOnly.MaxValue).ToList();
    }

    private static void CheckPlanned(List<GardenAlert> alerts, GardenBed bed, BedPlanting p, PlantRecord plant, string name,
        Garden? garden, IReadOnlyList<SeedItem> seeds, DateOnly today)
    {
        var plan = PlantingCalculator.Calculate(plant, garden, today.Year);
        if (!plan.Personalized)
        {
            alerts.Add(new(AlertSeverity.Info, "alert_need_frost", new[] { name }, null, p.Id, plant.Id, "no frost date or no sowing data for this plant"));
            return;
        }
        var windows = plan.Windows.Where(w => w.Activity != PlantingActivity.SowIndoors || plan.Windows.Count == 1).ToList();
        if (windows.Count == 0) windows = plan.Windows.ToList();
        var open = windows.FirstOrDefault(w => w.Contains(today));
        if (open is not null)
        {
            var left = open.End.DayNumber - today.DayNumber;
            alerts.Add(new(left <= 7 ? AlertSeverity.Urgent : AlertSeverity.Soon,
                left <= 7 ? "alert_window_closing" : "alert_window_open", new[] { name, bed.Name, open.End.ToString("d MMM") }, open.End, p.Id, plant.Id,
                $"estimated window {open.Start:d MMM}–{open.End:d MMM} from frost date"));
            if (!seeds.Any(s => s.PlantId == plant.Id && s.QuantityRemaining > 0))
                alerts.Add(new(AlertSeverity.Info, "alert_no_seed", new[] { name }, open.End, p.Id, plant.Id, "no seed packet with remaining quantity for this plant"));
            return;
        }
        var next = windows.Where(w => w.Start > today).OrderBy(w => w.Start).FirstOrDefault();
        if (next is not null && next.Start.DayNumber - today.DayNumber <= 14)
            alerts.Add(new(AlertSeverity.Soon, "alert_window_soon", new[] { name, bed.Name, next.Start.ToString("d MMM") }, next.Start, p.Id, plant.Id, "window opens within two weeks"));
        else if (next is null)
            alerts.Add(new(AlertSeverity.Info, "alert_window_passed", new[] { name }, null, p.Id, plant.Id, "this year's estimated window has passed"));
    }

    private static void CheckGrowing(List<GardenAlert> alerts, GardenBed bed, BedPlanting p, PlantRecord plant, string name, Garden? garden, DateOnly today)
    {
        if (p.PlantedDate is { } planted && plant.DaysToMaturity is { } d)
        {
            var from = planted.AddDays(d.Min); var to = planted.AddDays(d.Max);
            if (p.Status != PlantingStatus.Harvesting && today >= from && today <= to)
                alerts.Add(new(AlertSeverity.Soon, "alert_harvest_check", new[] { name, bed.Name }, from, p.Id, plant.Id, $"{d.Min}-{d.Max} days to maturity from {planted:d MMM}"));
            else if (today > to.AddDays(14))
                alerts.Add(new(AlertSeverity.Soon, "alert_harvest_late", new[] { name, bed.Name }, to, p.Id, plant.Id, "past the expected maturity range"));
            else if (today < from && from.DayNumber - today.DayNumber <= 14)
                alerts.Add(new(AlertSeverity.Info, "alert_harvest_soon", new[] { name, from.ToString("d MMM") }, from, p.Id, plant.Id, "maturity range starts within two weeks"));
        }
        if (plant.FrostTolerance == "tender" && garden?.FirstAutumnFrost is { } ff)
        {
            var frost = PlantingCalculator.SafeDate(today.Year, ff.Month, ff.Day);
            var days = frost.DayNumber - today.DayNumber;
            if (days is >= 0 and <= 10)
                alerts.Add(new(AlertSeverity.Urgent, "alert_frost_near", new[] { name, bed.Name, frost.ToString("d MMM") }, frost, p.Id, plant.Id, "tender plant and first-frost date within 10 days"));
        }
    }

    private static void CheckSun(List<GardenAlert> alerts, GardenBed bed, BedPlanting p, PlantRecord plant, string name, Garden? garden, DateOnly today)
    {
        if (garden?.Latitude is not { } lat || garden.Longitude is not { } lon || plant.Sun is null) return;
        // Growing season (April to September) average day length, reduced by the bed's shade estimate.
        var offset = SolarCalculator.GuessOffset(lon, today);
        var days = Enumerable.Range(4, 6).Select(m => SolarCalculator.Day(lat, lon, new DateOnly(today.Year, m, 15), offset).DayLengthHours).ToList();
        var hours = SolarCalculator.DirectSunHours(lat >= 0 ? days.Average() : 12, bed.ShadePercent);
        var need = plant.Sun == "full" ? 6 : plant.Sun == "full-partial" ? 4 : 0;
        if (need > 0 && hours < need)
            alerts.Add(new(AlertSeverity.Soon, "alert_sun_low", new[] { name, bed.Name, hours.ToString("0.#"), need.ToString() }, null, p.Id, plant.Id,
                $"day length at {lat:0.0}° minus {bed.ShadePercent}% shade, clear-sky assumption"));
    }
}
