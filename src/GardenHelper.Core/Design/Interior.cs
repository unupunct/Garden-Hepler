using GardenHelper.Core.Site;

namespace GardenHelper.Core.Design;

/// <summary>Something drawn on a floor plan inside a building: a wall, window, door, room, piece of furniture or a plant. Sizes and positions are centimetres; X/Y is the top-left corner before rotation.</summary>
public class InteriorItem
{
    public int Id { get; set; }
    /// <summary>The building (a structure place on the property map).</summary>
    public int FeatureId { get; set; }
    /// <summary>-1 basement, 0 ground floor, 1 upstairs, 2 attic.</summary>
    public int Floor { get; set; }
    public string Kind { get; set; } = "wall";
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 100;
    public double Height { get; set; } = 15;
    /// <summary>Clockwise rotation in degrees about the item's centre.</summary>
    public double Rotation { get; set; }
    /// <summary>The plant in the inventory this marker stands for (kind "plant").</summary>
    public int? InventoryPlantId { get; set; }
    public string? Notes { get; set; }
}

public sealed record InteriorKind(string Key, string Group, double Width, double Height, string Color, string Glyph, int Layer);

public static class InteriorCatalog
{
    public static readonly string[] Groups = { "structure", "rooms", "furniture", "greenhouse", "workshop", "shapes", "plants" };
    public static readonly int[] Floors = { -1, 0, 1, 2 };
    public const double WallThickness = 15;

    public static readonly InteriorKind[] All =
    {
        new("wall", "structure", 300, WallThickness, "#5A5046", "▬", 3), new("window", "structure", 120, 12, "#6EC1E4", "▭", 4),
        new("door", "structure", 90, 12, "#A0693A", "◜", 4), new("stairs", "structure", 100, 260, "#B9A98C", "≣", 2),

        new("room", "rooms", 400, 300, "#E8DFC8", "▢", 0),

        new("bed", "furniture", 160, 200, "#C9B4D6", "▭", 1), new("sofa", "furniture", 200, 90, "#9DB4C0", "▭", 1), new("table", "furniture", 160, 90, "#C8A778", "▭", 1),
        new("desk", "furniture", 140, 70, "#C8A778", "▭", 1), new("counter", "furniture", 240, 60, "#B8B8B0", "▭", 1), new("stove", "furniture", 60, 60, "#7C7C7C", "◎", 1),
        new("fridge", "furniture", 65, 65, "#D8DEE2", "❄", 1), new("sink", "furniture", 80, 50, "#A9CCD8", "◍", 1), new("wardrobe", "furniture", 120, 60, "#A98B66", "▭", 1),
        new("shelf", "furniture", 100, 30, "#A98B66", "▭", 1), new("radiator", "furniture", 100, 12, "#E0E0E0", "≋", 1), new("woodstove", "furniture", 70, 70, "#4F4F4F", "♨", 1),

        new("bench", "greenhouse", 120, 50, "#EBD7AE", "▭", 1),
        new("growbed", "greenhouse", 120, 60, "#D3EAC2", "❧", 1),
        new("pots", "greenhouse", 90, 40, "#E4CFB4", "❀", 1),
        new("seedtray", "greenhouse", 60, 40, "#CFE6A8", "⁘", 1),
        new("channel", "greenhouse", 200, 20, "#BDE0F0", "≈", 1),
        new("tank", "greenhouse", 80, 80, "#9FCFEA", "◍", 1),
        new("heater", "greenhouse", 40, 40, "#F6C5B4", "♨", 1),
        new("sensor", "greenhouse", 30, 30, "#FFE38A", "◉", 5),
        new("light", "greenhouse", 80, 14, "#FFF1A8", "☀", 4),
        new("fan", "greenhouse", 30, 30, "#D8E2EA", "✻", 4),
        new("mister", "greenhouse", 30, 30, "#CFEAF2", "∴", 4),
        new("vent", "greenhouse", 80, 14, "#DDE3E8", "⬆", 4),
        new("pump", "greenhouse", 30, 30, "#D9CFEE", "↻", 5),
        new("car", "workshop", 200, 450, "#B7C4D6", "▭", 1),
        new("workbench", "workshop", 200, 70, "#C8A778", "▭", 1),
        new("toolrack", "workshop", 150, 30, "#A98B66", "⚒", 1),
        new("custom", "shapes", 200, 150, "#E6E0D2", "▢", 1),
        new("customround", "shapes", 150, 150, "#E6E0D2", "○", 1),

        new("plant", "plants", 50, 50, "#4F9A4F", "❀", 5), new("planter", "plants", 120, 40, "#7BA05B", "▤", 5),
    };

    public static InteriorKind? Get(string key) => All.FirstOrDefault(k => k.Key == key);
    public static IEnumerable<InteriorKind> InGroup(string group) => All.Where(k => k.Group == group);
    public static (double Width, double Height) SizeOf(string key) => Get(key) is { } k ? (k.Width, k.Height) : (100, 100);
    public static bool IsRound(string key) => key is "plant" or "customround";
    public static bool IsOpening(string key) => key is "window" or "door";

    public static string FloorKey(int floor) => floor switch { < 0 => "basement", 0 => "ground", 1 => "upper", _ => "attic" };

    /// <summary>Width and height in centimetres of the box around a building's outline, with room to spare for walls.</summary>
    public static (double Width, double Height) Footprint(IReadOnlyList<GeoPoint> outline)
    {
        if (outline.Count < 3) return (1000, 800);
        var proj = new LocalProjection(outline[0]);
        var pts = outline.Select(proj.ToMetres).ToList();
        var w = (pts.Max(p => p.X) - pts.Min(p => p.X)) * 100; var h = (pts.Max(p => p.Y) - pts.Min(p => p.Y)) * 100;
        return (Math.Clamp(Math.Round(w / 10) * 10 + 100, 400, 6000), Math.Clamp(Math.Round(h / 10) * 10 + 100, 400, 6000));
    }

    /// <summary>The building's outline in the same centimetre space as the floor plan (matching <see cref="Footprint"/>): 50 cm in from the top-left corner of its box.</summary>
    public static List<(double X, double Y)> OutlineCm(IReadOnlyList<GeoPoint> outline)
    {
        if (outline.Count < 3) return new();
        var proj = new LocalProjection(outline[0]);
        var pts = outline.Select(proj.ToMetres).ToList();
        var minX = pts.Min(p => p.X); var maxY = pts.Max(p => p.Y);
        return pts.Select(p => (50 + (p.X - minX) * 100, 50 + (maxY - p.Y) * 100)).ToList();
    }

    /// <summary>
    /// Puts a window or door onto the closest wall (within <paramref name="reach"/> cm of its centre): same angle as the wall, centred on it.
    /// Returns false, changing nothing, when there is no wall that close.
    /// </summary>
    public static bool SnapToWall(InteriorItem opening, IEnumerable<InteriorItem> walls, double reach = 40)
    {
        var cx = opening.X + opening.Width / 2; var cy = opening.Y + opening.Height / 2;
        InteriorItem? best = null; double bestD = reach; (double X, double Y) bestPoint = default;
        foreach (var w in walls.Where(w => w.Kind == "wall" && w.Floor == opening.Floor))
        {
            var r = w.Rotation * Math.PI / 180; var ux = Math.Cos(r); var uy = Math.Sin(r);
            var wx = w.X + w.Width / 2; var wy = w.Y + w.Height / 2;
            var along = Math.Clamp((cx - wx) * ux + (cy - wy) * uy, -w.Width / 2 + opening.Width / 2, w.Width / 2 - opening.Width / 2);
            var px = wx + ux * along; var py = wy + uy * along;
            var d = Math.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));
            if (d < bestD) { bestD = d; best = w; bestPoint = (px, py); }
        }
        if (best is null) return false;
        opening.Rotation = best.Rotation; opening.Height = Math.Max(best.Height, 12);
        opening.X = bestPoint.X - opening.Width / 2; opening.Y = bestPoint.Y - opening.Height / 2;
        return true;
    }

    /// <summary>A wall running from one point to another: the rectangle (centre-rotation convention) that covers that line.</summary>
    public static InteriorItem WallBetween(int featureId, int floor, double x1, double y1, double x2, double y2, double thickness = WallThickness)
    {
        var len = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        var angle = Math.Atan2(y2 - y1, x2 - x1) * 180 / Math.PI;
        var cx = (x1 + x2) / 2; var cy = (y1 + y2) / 2;
        return new InteriorItem { FeatureId = featureId, Floor = floor, Kind = "wall", Width = len, Height = thickness, X = cx - len / 2, Y = cy - thickness / 2, Rotation = Math.Round(angle, 1) };
    }

    /// <summary>Rounds to a grid step (default 10 cm).</summary>
    public static double Snap(double v, double step = 10) => Math.Round(v / step) * step;
}
