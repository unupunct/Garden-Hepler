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
