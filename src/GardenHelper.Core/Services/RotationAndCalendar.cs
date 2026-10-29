using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;

namespace GardenHelper.Core.Services;

public enum RotationVerdict { Compatible, Caution, Conflict, Insufficient, NotApplicable }

/// <param name="Reason">Localized-key-free explanation data: which rule, which earlier crop, how many years.</param>
public sealed record RotationCheck(RotationVerdict Verdict, string Key, string[] Args, string? RuleReason);

/// <summary>
/// Checks a candidate crop against a bed's planting history using the family rules in rotation-rules.json.
/// It advises; it never blocks. Callers record an override when the user proceeds anyway.
/// </summary>
public static class RotationAdvisor
{
    public static RotationCheck Check(PlantRecord candidate, int year, IEnumerable<BedPlanting> history, KnowledgeBase kb, string lang = "en")
    {
        var name = candidate.DisplayName(lang);
        if (candidate.Lifecycle is { } life && life.StartsWith("perennial", StringComparison.OrdinalIgnoreCase) && candidate.Category != "vegetable")
            return new(RotationVerdict.NotApplicable, "rot_perennial", new[] { name }, null);
        var family = candidate.RotationFamily ?? candidate.Family;
        var rule = kb.RuleFor(family);
        if (rule is null)
            return new(RotationVerdict.Insufficient, "rot_no_rule", new[] { name, family ?? "?" }, null);

        var past = history.Select(h => (Planting: h, Plant: kb.Get(h.PlantId)))
            .Where(x => x.Plant is not null && string.Equals(x.Plant!.RotationFamily ?? x.Plant.Family, family, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var allHistory = history.ToList();
        if (allHistory.Count == 0)
            return new(RotationVerdict.Insufficient, "rot_no_history", new[] { name }, null);

        var undated = past.Where(x => x.Planting.PlantedDate is null).ToList();
        var dated = past.Where(x => x.Planting.PlantedDate is not null).ToList();
        if (dated.Count > 0)
        {
            var last = dated.OrderByDescending(x => x.Planting.PlantedDate).First();
            var gap = year - last.Planting.PlantedDate!.Value.Year;
            var lastName = last.Plant!.DisplayName(lang);
            if (gap < 0) return new(RotationVerdict.Insufficient, "rot_future_history", new[] { lastName }, null);
            if (gap == 0)
                return new(RotationVerdict.Caution, "rot_same_year", new[] { name, lastName, family! }, rule.ReasonText(lang));
            if (gap < rule.MinYears)
                return new(RotationVerdict.Conflict, "rot_too_soon", new[] { name, lastName, gap.ToString(), rule.MinYears.ToString(), family! }, rule.ReasonText(lang));
            return new(RotationVerdict.Compatible, "rot_ok", new[] { name, family!, gap.ToString(), rule.MinYears.ToString() }, rule.ReasonText(lang));
        }
        if (undated.Count > 0)
            return new(RotationVerdict.Insufficient, "rot_undated", new[] { name, undated[0].Plant!.DisplayName(lang) }, rule.ReasonText(lang));
        return new(RotationVerdict.Compatible, "rot_family_new", new[] { name, family! }, rule.ReasonText(lang));
    }
}

public sealed record MonthActivity(int Month, string Kind, string PlantId, string PlantName, int BedId, string BedName, DateOnly Start, DateOnly End);

/// <summary>Turns beds' plantings into the activities that fall in a month (sowing windows, expected harvests).</summary>
public static class CalendarPlanner
{
    public static List<MonthActivity> ForYear(Garden? garden, IEnumerable<GardenBed> beds, KnowledgeBase kb, int year, string lang = "en")
    {
        var result = new List<MonthActivity>();
        foreach (var bed in beds)
            foreach (var p in bed.Plantings.Where(p => p.Status != PlantingStatus.Finished))
            {
                var plant = kb.Get(p.PlantId);
                if (plant is null) continue;
                var name = plant.DisplayName(lang);
                if (p.Status == PlantingStatus.Planned)
                    foreach (var w in PlantingCalculator.Calculate(plant, garden, year).Windows)
                        AddSpanned(result, w.Activity.ToString(), plant.Id, name, bed, w.Start, w.End, year);
                if (p.PlantedDate is { } d && plant.DaysToMaturity is { } m)
                    AddSpanned(result, "Harvest", plant.Id, name, bed, d.AddDays(m.Min), d.AddDays(m.Max), year);
            }
        return result.OrderBy(a => a.Start).ToList();
    }

    public static List<MonthActivity> ForMonth(IEnumerable<MonthActivity> all, int month) => all.Where(a => a.Month == month).ToList();

    private static void AddSpanned(List<MonthActivity> list, string kind, string plantId, string name, GardenBed bed, DateOnly start, DateOnly end, int year)
    {
        // One entry per calendar month the span touches, restricted to the requested year.
        for (var m = new DateOnly(start.Year, start.Month, 1); m <= end; m = m.AddMonths(1))
        {
            if (m.Year != year) continue;
            var monthEnd = new DateOnly(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month));
            list.Add(new MonthActivity(m.Month, kind, plantId, name, bed.Id, bed.Name, start > m ? start : m, end < monthEnd ? end : monthEnd));
        }
    }
}
