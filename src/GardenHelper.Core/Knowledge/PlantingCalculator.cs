using GardenHelper.Core.Domain;

namespace GardenHelper.Core.Knowledge;

public enum PlantingActivity { SowIndoors, SowDirect, Transplant, Plant }

public sealed record PlantingWindow(PlantingActivity Activity, DateOnly Start, DateOnly End)
{
    public bool Contains(DateOnly d) => d >= Start && d <= End;
}

public sealed record PlantingPlan(IReadOnlyList<PlantingWindow> Windows, bool Personalized, string? Reason);

/// <summary>
/// Turns a plant's frost-relative sowing weeks into calendar dates. Results are estimated windows, never guarantees:
/// soil temperature, weather and variety all shift the real dates.
/// </summary>
public static class PlantingCalculator
{
    public static PlantingPlan Calculate(PlantRecord plant, Garden? garden, int year, int shiftDays = 0)
    {
        var sowing = plant.Sowing;
        if (sowing is null || (sowing.Indoors is null && sowing.Direct is null && sowing.Transplant is null && sowing.Plant is null))
            return new PlantingPlan(Array.Empty<PlantingWindow>(), false, "noData");
        var frost = sowing.Anchor == "firstFrost" ? garden?.FirstAutumnFrost : garden?.LastSpringFrost;
        if (frost is null)
            return new PlantingPlan(Array.Empty<PlantingWindow>(), false, "noFrostDate");

        var anchor = SafeDate(year, frost.Value.Month, frost.Value.Day);
        var windows = new List<PlantingWindow>();
        void Add(PlantingActivity a, IntRange? r)
        {
            if (r is null) return;
            windows.Add(new PlantingWindow(a, anchor.AddDays(r.Min * 7 + shiftDays), anchor.AddDays(r.Max * 7 + shiftDays)));
        }
        Add(PlantingActivity.SowIndoors, sowing.Indoors);
        Add(PlantingActivity.SowDirect, sowing.Direct);
        Add(PlantingActivity.Transplant, sowing.Transplant);
        Add(PlantingActivity.Plant, sowing.Plant);
        return new PlantingPlan(windows.OrderBy(w => w.Start).ToList(), true, null);
    }

    public static DateOnly SafeDate(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    public static string Season(DateOnly date, bool southernHemisphere = false)
    {
        var s = date.Month switch { 12 or 1 or 2 => 0, 3 or 4 or 5 => 1, 6 or 7 or 8 => 2, _ => 3 };
        if (southernHemisphere) s = (s + 2) % 4;
        return s switch { 0 => "winter", 1 => "spring", 2 => "summer", _ => "autumn" };
    }
}
