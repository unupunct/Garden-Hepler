using System.Globalization;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;

namespace GardenHelper.Core.Planning;

/// <summary>What to get ready for in the coming days: sowings about to open, seasonal care for fruit trees, and winter preparation as the first frost nears.</summary>
public static class LookAhead
{
    private const string Section = "ahead";
    private static readonly string[] StoneFruit = { "cherry", "sweet-cherry", "sour-cherry", "plum", "apricot", "peach" };

    public static List<TodayItem> Build(DateOnly today, Garden? garden, IEnumerable<PlantRecord> crops, IEnumerable<InventoryPlant> trees, IEnumerable<GardenTask> tasks,
        Func<PlantRecord, string> nameOf, Func<PlantingActivity, string> actOf, CultureInfo culture)
    {
        var items = new List<TodayItem>();
        var open = tasks.Where(t => !t.Done).ToList();

        // sowings and plantings that open this week, or are open now
        foreach (var crop in crops)
        {
            foreach (var year in new[] { today.Year, today.Year + 1 })
                foreach (var w in PlantingCalculator.Calculate(crop, garden, year).Windows)
                {
                    if (w.End < today || w.Start > today.AddDays(7)) continue;
                    if (open.Any(t => t.PlantId == crop.Id && t.DueDate >= w.Start.AddDays(-7) && t.DueDate <= w.End)) continue;     // already on the to-do list
                    var opening = w.Start > today;
                    items.Add(new(Section, opening ? "info" : "soon", "🌱", opening ? "look_sow_opens" : "look_sow_now",
                        new[] { actOf(w.Activity), nameOf(crop), (opening ? w.Start : w.End).ToString("d MMM", culture) }, TodayAction.AddTask, 0, PlantId: crop.Id, Due: opening ? w.Start : today));
                    break;
                }
        }

        // fruit trees: the windows follow this garden's frost dates (a cold valley is weeks behind a warm plain); without frost dates, calendar months are used
        var fruit = trees.Where(t => t.IsTree && t.Status != "dead" && (t.TreeType == "fruit")).ToList();
        if (fruit.Count > 0)
        {
            var stone = fruit.Count(t => t.SpeciesId is { } s && StoneFruit.Contains(s));
            var notStone = (fruit.Count - stone).ToString();     // stone fruit is pruned in summer, not in winter (silver leaf and gummosis enter through winter cuts)
            var all = fruit.Count.ToString();
            var spring = garden?.LastSpringFrost; var autumn = garden?.FirstAutumnFrost;
            var m = today.Month; var d = today.Day;
            void Tree(string key, string count, DateOnly due) { if (!open.Any(t => t.Title.Contains(key) && t.DueDate >= today.AddDays(-30))) items.Add(new(Section, "info", "🌳", key, new[] { count }, TodayAction.AddTask, 0, Due: due)); }
            bool Near(DateOnly? anchor, int from, int to, bool fallback)
            {
                if (anchor is not { } a) return fallback;
                foreach (var year in new[] { today.Year - 1, today.Year, today.Year + 1 })
                {
                    var day = new DateOnly(year, a.Month, Math.Min(a.Day, DateTime.DaysInMonth(year, a.Month)));
                    if (today >= day.AddDays(from) && today <= day.AddDays(to)) return true;
                }
                return false;
            }
            if (fruit.Count - stone > 0 && Near(spring, -130, -56, m is 1 or 2 || (m == 3 && d <= 15))) Tree("look_tree_prune_winter", notStone, today);
            if (Near(spring, -84, -40, (m == 2 && d >= 15) || m == 3)) Tree("look_tree_dormant_spray", all, today);
            if (Near(spring, 30, 56, m == 6 || (m == 7 && d <= 5))) Tree("look_tree_thin", all, today);
            if (stone > 0 && Near(spring, 66, 113, (m == 7 && d >= 15) || m == 8)) Tree("look_tree_summer_prune", stone.ToString(), today);
            if (Near(autumn, 15, 56, (m == 10 && d >= 20) || m == 11)) Tree("look_tree_whitewash", all, today);
        }

        // winter preparation around the first autumn frost
        if (garden?.FirstAutumnFrost is { } fa)
        {
            var frost = new DateOnly(today.Year, fa.Month, fa.Day);
            var days = frost.DayNumber - today.DayNumber;
            if (days is <= 21 and >= -14)
            {
                var sev = days <= 7 ? "soon" : "info";
                var when = frost.ToString("d MMM", culture);
                items.Add(new(Section, sev, "❄", "look_winter_protect", new[] { when }, TodayAction.AddTask, 0, Due: today));
                items.Add(new(Section, sev, "🍂", "look_winter_mulch", new[] { when }, TodayAction.AddTask, 0, Due: today.AddDays(3)));
                items.Add(new(Section, sev, "🚿", "look_winter_water", new[] { when }, TodayAction.AddTask, 0, Due: today.AddDays(5)));
            }
        }
        return items;
    }
}
