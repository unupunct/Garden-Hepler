using System.Globalization;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Site;

namespace GardenHelper.Core.Planning;

/// <summary>A planting that is on its way to harvest: when it should be ready, from its planting date and the plant's days to maturity.</summary>
public sealed record Ripening(int PlantingId, string PlantId, string BedName, DateOnly Earliest, DateOnly Latest, int DaysToEarliest, bool ReadyNow);

public static class HarvestForecast
{
    /// <summary>Plantings sown or growing that reach maturity within <paramref name="horizonDays"/> (or already have), soonest first.</summary>
    public static List<Ripening> Upcoming(IEnumerable<GardenBed> beds, KnowledgeBase kb, DateOnly today, int horizonDays = 45)
    {
        var list = new List<Ripening>();
        foreach (var bed in beds.Where(b => !FeatureCatalog.IsStructure(b.Kind) && b.Kind is not ("path" or "pond" or "compost")))
            foreach (var p in bed.Plantings.Where(p => p.Status is PlantingStatus.Sown or PlantingStatus.Growing && p.PlantedDate is not null))
            {
                if (kb.Get(p.PlantId)?.DaysToMaturity is not { } m) continue;
                var planted = p.PlantedDate!.Value; var first = planted.AddDays(m.Min); var last = planted.AddDays(m.Max);
                if (last < today.AddDays(-30) || first > today.AddDays(horizonDays)) continue;     // long gone or too far off
                list.Add(new Ripening(p.Id, p.PlantId, bed.Name, first, last, first.DayNumber - today.DayNumber, first <= today));
            }
        return list.OrderBy(r => r.Earliest).ThenBy(r => r.BedName).ToList();
    }
}

/// <summary>Plain-language advice from the next few days of forecast.</summary>
public static class WeatherAdvice
{
    public static List<TodayItem> Evaluate(IEnumerable<WeatherDay> days, DateOnly today, CultureInfo culture)
    {
        var items = new List<TodayItem>();
        foreach (var d in days.Where(d => d.Date >= today && d.Date <= today.AddDays(3)).OrderBy(d => d.Date))
        {
            var when = d.Date == today ? "" : d.Date.ToString("ddd d MMM", culture);
            var near = d.Date <= today.AddDays(1);
            if (d.MinC <= 1) items.Add(new("advice", near ? "urgent" : "soon", "❄", "today_wx_frost", new[] { when, d.MinC.ToString("0", culture) }, TodayAction.OpenActions, 0));
            if (d.MaxC >= 32) items.Add(new("advice", "soon", "☀", "today_wx_heat", new[] { when, d.MaxC.ToString("0", culture) }, TodayAction.OpenActions, 0));
            if (d.WindMaxKmh >= 50) items.Add(new("advice", "soon", "💨", "today_wx_wind", new[] { when, d.WindMaxKmh.ToString("0", culture) }, TodayAction.OpenActions, 0));
            if (d.Date == today && d.PrecipMm >= 5 && d.PrecipProbPct >= 60) items.Add(new("advice", "info", "☂", "today_wx_rain", new[] { d.PrecipMm.ToString("0", culture) }, TodayAction.OpenActions, 0));
        }
        return items;
    }
}

/// <summary>What a bed looks like for watering: its soil sensor (if any), when something in it was last watered, and whether anything is growing in it.</summary>
public sealed record BedWatering(int BedId, string BedName, double? MoisturePercent, DateOnly? LastWatered, bool HasGrowing);

/// <summary>Decides which beds need water today, using the soil sensor when there is one and the forecast either way. Rain that is coming cancels the chore.</summary>
public static class WateringAdvisor
{
    public static bool RainExpected(IEnumerable<WeatherDay> days, DateOnly today) =>
        days.Any(d => (d.Date == today && d.PrecipMm >= 5 && d.PrecipProbPct >= 60) || (d.Date == today.AddDays(1) && d.PrecipMm >= 8 && d.PrecipProbPct >= 70));

    public static List<TodayItem> Evaluate(IEnumerable<BedWatering> beds, IEnumerable<WeatherDay> days, DateOnly today, CultureInfo culture)
    {
        var forecast = days.ToList();
        var rain = RainExpected(forecast, today);
        var hot = forecast.Any(d => d.Date == today && d.MaxC >= 28);
        var items = new List<TodayItem>();
        foreach (var b in beds.Where(b => b.HasGrowing))
        {
            if (b.MoisturePercent is { } m)
            {
                if (m >= 25) continue;
                if (rain) items.Add(new("plants", "info", "☂", "today_bed_dry_rain", new[] { b.BedName, m.ToString("0", culture) }, TodayAction.OpenActions, b.BedId));
                else items.Add(new("plants", m < 15 ? "urgent" : "soon", "💧", "today_bed_dry", new[] { b.BedName, m.ToString("0", culture) }, TodayAction.WaterBed, b.BedId));
            }
            else if (hot && !rain)
            {
                var since = b.LastWatered is { } w ? today.DayNumber - w.DayNumber : (int?)null;
                if (since is null || since >= 3)
                    items.Add(new("plants", "soon", "💧", since is null ? "today_bed_hot_never" : "today_bed_hot", since is null ? new[] { b.BedName } : new[] { b.BedName, since.ToString()! }, TodayAction.WaterBed, b.BedId));
            }
        }
        return Merge(items);
    }

    /// <summary>Beds that share the same advice and reading (one sensor for a whole garden) become one line naming them, with all their ids.</summary>
    private static List<TodayItem> Merge(List<TodayItem> items)
    {
        var merged = new List<TodayItem>();
        foreach (var g in items.GroupBy(i => (i.Key, i.Args.Length > 1 ? i.Args[1] : "")))
        {
            var list = g.ToList();
            if (list.Count == 1) { merged.Add(list[0]); continue; }
            var names = list.Select(i => i.Args[0]).ToList();
            var label = string.Join(", ", names.Take(3)) + (names.Count > 3 ? $" +{names.Count - 3}" : "");
            var args = (string[])list[0].Args.Clone(); args[0] = label;
            merged.Add(list[0] with { Args = args, RefIds = list.Select(i => i.RefId).ToArray(), Severity = list.Min(i => TodayPlanner.SeverityRank(i.Severity)) == 0 ? "urgent" : list[0].Severity });
        }
        return merged;
    }
}
