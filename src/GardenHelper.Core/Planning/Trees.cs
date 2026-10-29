using GardenHelper.Core.Domain;

namespace GardenHelper.Core.Planning;

/// <summary>What a tree does to a nearby bed: how much of the bed its crown covers and whether its roots reach it.</summary>
public sealed record TreeBedInteraction(int BedId, string BedName, double ShadePercent, bool RootsReach, double DistanceCm);

public static class TreeRules
{
    public static readonly string[] Types = { "fruit", "nut", "shade", "conifer", "other" };

    /// <summary>Roots of most trees reach about as far as the crown, and often further; 1.5 x the crown radius is a cautious default.</summary>
    public static double RootRadius(double crownRadiusCm, double? rootRadiusCm) => rootRadiusCm is > 0 ? rootRadiusCm.Value : crownRadiusCm * 1.5;

    /// <summary>Shortest distance from a point to a (possibly rotated) rectangle given by its top-left corner, size and rotation about its centre. Zero when inside.</summary>
    public static double DistanceToRect(double px, double py, double x, double y, double w, double h, double rotationDeg)
    {
        var (cx, cy) = (x + w / 2, y + h / 2);
        var a = -rotationDeg * Math.PI / 180;                       // undo the bed rotation so the rectangle is axis aligned
        var (dx, dy) = (px - cx, py - cy);
        var (lx, ly) = (dx * Math.Cos(a) - dy * Math.Sin(a), dx * Math.Sin(a) + dy * Math.Cos(a));
        var (ox, oy) = (Math.Max(Math.Abs(lx) - w / 2, 0), Math.Max(Math.Abs(ly) - h / 2, 0));
        return Math.Sqrt(ox * ox + oy * oy);
    }

    /// <summary>Share (0-100) of the bed's area lying under the crown circle, sampled on a 24 x 24 grid.</summary>
    public static double ShadePercent(double tx, double ty, double crownRadiusCm, double x, double y, double w, double h, double rotationDeg)
    {
        if (crownRadiusCm <= 0 || w <= 0 || h <= 0) return 0;
        const int n = 24;
        var (cx, cy) = (x + w / 2, y + h / 2);
        var a = rotationDeg * Math.PI / 180;
        var inside = 0;
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
            {
                var (lx, ly) = ((i + 0.5) / n * w - w / 2, (j + 0.5) / n * h - h / 2);
                var (wx, wy) = (cx + lx * Math.Cos(a) - ly * Math.Sin(a), cy + lx * Math.Sin(a) + ly * Math.Cos(a));
                if ((wx - tx) * (wx - tx) + (wy - ty) * (wy - ty) <= crownRadiusCm * crownRadiusCm) inside++;
            }
        return 100.0 * inside / (n * n);
    }

    /// <summary>Crown shade and root reach of one tree on every bed that is touched by either. Strongest shade first.</summary>
    public static List<TreeBedInteraction> Interactions(double treeX, double treeY, double crownRadiusCm, double? rootRadiusCm, IEnumerable<GardenBed> beds)
    {
        var root = RootRadius(crownRadiusCm, rootRadiusCm);
        var list = new List<TreeBedInteraction>();
        foreach (var b in beds)
        {
            var d = DistanceToRect(treeX, treeY, b.X, b.Y, b.Width, b.Height, b.Rotation);
            var shade = ShadePercent(treeX, treeY, crownRadiusCm, b.X, b.Y, b.Width, b.Height, b.Rotation);
            var reach = d <= root;
            if (shade > 0 || reach) list.Add(new TreeBedInteraction(b.Id, b.Name, Math.Round(shade), reach, d));
        }
        return list.OrderByDescending(i => i.ShadePercent).ThenBy(i => i.DistanceCm).ToList();
    }

    // Care calendar for the northern hemisphere (the app's default); keys are translated as "tact_" + key.
    private static readonly (string Key, int[] Months, string[] Types)[] Calendar =
    {
        ("prune_dormant", new[] { 1, 2, 3 }, new[] { "fruit", "nut", "shade" }),
        ("dormant_spray", new[] { 2, 3 }, new[] { "fruit" }),
        ("spring_feed", new[] { 3, 4 }, new[] { "fruit", "nut", "shade", "conifer", "other" }),
        ("mulch", new[] { 4, 5 }, new[] { "fruit", "nut", "shade", "conifer", "other" }),
        ("check_pests", new[] { 5, 6, 7 }, new[] { "fruit", "nut", "shade", "conifer", "other" }),
        ("thin_fruit", new[] { 6 }, new[] { "fruit" }),
        ("deep_water", new[] { 7, 8 }, new[] { "fruit", "nut", "shade", "conifer", "other" }),
        ("summer_prune", new[] { 7, 8 }, new[] { "fruit" }),
        ("harvest", new[] { 8, 9, 10 }, new[] { "fruit", "nut" }),
        ("clear_fallen", new[] { 9, 10, 11 }, new[] { "fruit", "nut" }),
        ("whitewash", new[] { 11 }, new[] { "fruit" }),
        ("protect_young", new[] { 11, 12 }, new[] { "fruit", "nut", "shade", "conifer", "other" }),
    };

    public static IEnumerable<string> AllActionKeys => Calendar.Select(c => c.Key);

    /// <summary>The care jobs that belong to this month for a tree of this type. Young trees (under 3 years) also get winter protection and extra watering.</summary>
    public static List<string> SeasonalActions(string? type, int month, double? ageYears)
    {
        var t = Types.Contains(type ?? "") ? type! : "other";
        var young = ageYears is < 3;
        return Calendar.Where(c => c.Months.Contains(month) && c.Types.Contains(t))
                       .Where(c => c.Key != "protect_young" || young)
                       .Select(c => c.Key).ToList();
    }

    public static double? AgeYears(DateOnly? plantedOn, DateOnly today) => plantedOn is null ? null : (today.DayNumber - plantedOn.Value.DayNumber) / 365.25;
}
