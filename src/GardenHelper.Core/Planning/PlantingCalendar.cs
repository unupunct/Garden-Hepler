using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;

namespace GardenHelper.Core.Planning;

/// <summary>One coloured stretch on the planting calendar: kind is sow_indoors, sow_direct, transplant, plant or harvest. Days are days of the year (1-366).</summary>
public sealed record CalendarBar(string Kind, int StartDay, int EndDay);
public sealed record CalendarRow(string PlantId, IReadOnlyList<CalendarBar> Bars);

/// <summary>A year at a glance for chosen crops: when to sow indoors, sow outside, transplant, and when the harvest should come.</summary>
public static class PlantingCalendar
{
    public static List<CalendarRow> Build(IEnumerable<PlantRecord> plants, Garden? garden, int year)
    {
        var rows = new List<CalendarRow>();
        var first = new DateOnly(year, 1, 1); var last = new DateOnly(year, 12, 31);
        foreach (var plant in plants)
        {
            var plan = PlantingCalculator.Calculate(plant, garden, year);
            var bars = new List<CalendarBar>();
            foreach (var w in plan.Windows)
            {
                var kind = w.Activity switch { PlantingActivity.SowIndoors => "sow_indoors", PlantingActivity.SowDirect => "sow_direct", PlantingActivity.Transplant => "transplant", _ => "plant" };
                if (Clamp(w.Start, w.End, first, last) is { } c) bars.Add(new CalendarBar(kind, c.Start, c.End));
                if (w.Activity != PlantingActivity.SowIndoors && plant.DaysToMaturity is { } days && !(plant.Lifecycle ?? "").StartsWith("perennial", StringComparison.OrdinalIgnoreCase))
                {
                    if (Clamp(w.Start.AddDays(days.Min), w.End.AddDays(days.Max), first, last) is { } h) bars.Add(new CalendarBar("harvest", h.Start, h.End));
                }
            }
            // the harvest bars of several windows overlap: keep one stretch from the earliest start to the latest end
            var harvest = bars.Where(b => b.Kind == "harvest").ToList();
            if (harvest.Count > 1) { bars.RemoveAll(b => b.Kind == "harvest"); bars.Add(new CalendarBar("harvest", harvest.Min(b => b.StartDay), harvest.Max(b => b.EndDay))); }
            if (bars.Count > 0) rows.Add(new CalendarRow(plant.Id, bars));
        }
        return rows;
    }

    private static (int Start, int End)? Clamp(DateOnly start, DateOnly end, DateOnly first, DateOnly last)
    {
        if (end < first || start > last) return null;
        var s = start < first ? first : start; var e = end > last ? last : end;
        return (s.DayOfYear, e.DayOfYear);
    }
}
