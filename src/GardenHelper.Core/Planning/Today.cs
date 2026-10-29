using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;

namespace GardenHelper.Core.Planning;

public enum TodayAction { Done, Watered, WaterBed, LogProduction, OpenAnimals, OpenGreenhouse, OpenActions, OpenInventory }

/// <summary>One thing that needs the owner today. The text is a localisation key plus arguments; <see cref="RefId"/> says which task, plant, animal group or greenhouse it is about.</summary>
public sealed record TodayItem(string Section, string Severity, string Glyph, string Key, string[] Args, TodayAction Action, int RefId, bool NeedsNumber = false, string? Unit = null, int[]? RefIds = null);

/// <summary>Turns tasks, plants and animals into today's list: what is late, what is due, what has not been logged yet.</summary>
public static class TodayPlanner
{
    public static readonly string[] Sections = { "tasks", "plants", "animals", "greenhouse", "advice" };

    public static List<TodayItem> Build(DateOnly today, IEnumerable<GardenTask> tasks, IEnumerable<InventoryPlant> plants, IEnumerable<AnimalGroup> groups, string cultureDateFormat = "d MMM")
    {
        var items = new List<TodayItem>();

        foreach (var t in tasks.Where(t => !t.Done && t.DueDate <= today).OrderBy(t => t.DueDate))
        {
            var late = today.DayNumber - t.DueDate.DayNumber;
            items.Add(late > 0
                ? new("tasks", late >= 3 ? "urgent" : "soon", "✔", "today_task_late", new[] { t.Title, late.ToString() }, TodayAction.Done, t.Id)
                : new("tasks", "soon", "✔", "today_task_due", new[] { t.Title }, TodayAction.Done, t.Id));
        }

        foreach (var p in plants.Where(p => !p.IsTree && p.Status != "dead"))
        {
            if (InventoryRules.DaysOverdue(p, today) is { } d && d >= 0)
                items.Add(new("plants", d >= 3 ? "urgent" : "soon", "💧", d == 0 ? "today_water_due" : "today_water_late", new[] { p.Name, p.Place ?? "", d.ToString() }, TodayAction.Watered, p.Id));
            else if (p.Status == "attention")
                items.Add(new("plants", "info", "❀", "today_plant_attention", new[] { p.Name, p.Place ?? "" }, TodayAction.OpenInventory, p.Id));
        }

        foreach (var g in groups)
        {
            var unit = g.Species switch { "chicken" or "duck" => "eggs", "goat" => "milk", _ => null };
            if (unit is not null && g.Logs.Any(l => l.Kind == "production" && l.Date >= today.AddDays(-14)) && !g.Logs.Any(l => l.Kind == "production" && l.Date == today))
                items.Add(new("animals", "info", unit == "eggs" ? "🥚" : "🥛", unit == "eggs" ? "today_log_eggs" : "today_log_milk", new[] { g.Name }, TodayAction.LogProduction, g.Id, true, unit));
            if (AnimalRules.ActiveWithdrawal(g.Logs, today) is { } until)
                items.Add(new("animals", "soon", "⛔", "today_withdrawal", new[] { g.Name, until.ToString(cultureDateFormat) }, TodayAction.OpenAnimals, g.Id));
        }

        return items.OrderBy(i => SeverityRank(i.Severity)).ThenBy(i => Array.IndexOf(Sections, i.Section)).ToList();
    }

    public static int SeverityRank(string severity) => severity switch { "urgent" => 0, "soon" => 1, _ => 2 };
}
