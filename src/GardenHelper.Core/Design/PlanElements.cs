namespace GardenHelper.Core.Design;

/// <summary>One kind of thing that can be put on the garden plan: its group in the palette, default size in centimetres, colour and symbol.</summary>
public sealed record PlanElement(string Key, string Group, double Width, double Height, string Color, string Glyph, bool IsStructure = false);

/// <summary>Everything the plan palette offers. Structures are also places on the property (they sync with the map); the rest are plan objects.</summary>
public static class PlanElements
{
    public static readonly string[] Groups = { "growing", "boundaries", "utilities", "buildings", "paths", "devices" };

    public static readonly PlanElement[] All =
    {
        new("bed", "growing", 120, 300, "#8CAF64", "▦"), new("raised", "growing", 120, 240, "#966E46", "▤"), new("greenhouse", "growing", 300, 400, "#96D2DC", "⌂"),
        new("tree", "growing", 300, 300, "#3C783C", "♣"), new("pond", "growing", 400, 300, "#4682BE", "≈"),

        new("fence", "boundaries", 500, 12, "#7A5C3A", "┃"), new("gate", "boundaries", 120, 12, "#B08A4E", "⊓"),
        new("hedge", "boundaries", 500, 70, "#3F7A3F", "♠"), new("wall", "boundaries", 500, 25, "#8A8A8A", "▬"),

        new("compost", "utilities", 100, 100, "#6E5032", "♻"), new("bin", "utilities", 60, 60, "#4A5A4A", "🗑"), new("tank", "utilities", 150, 150, "#5B9BD5", "◍"),
        new("well", "utilities", 80, 80, "#6A8CA0", "◎"), new("solar", "utilities", 200, 100, "#2F4B7C", "☀"), new("hive", "utilities", 60, 60, "#E0B030", "⬢"),
        new("bench", "utilities", 150, 50, "#A67C52", "▭"),

        new("house", "buildings", 1000, 800, "#B06A4A", "⌂", true), new("workshop", "buildings", 500, 400, "#8C6F56", "⚒", true),
        new("shed", "buildings", 300, 300, "#9A846E", "▣", true), new("barn", "buildings", 1200, 600, "#9B4F3A", "▥", true),
        new("garage", "buildings", 600, 500, "#777782", "▤", true), new("coop", "buildings", 300, 200, "#B5835A", "🐔", true),
        new("pen", "buildings", 500, 400, "#A89B6A", "▧", true), new("other_structure", "buildings", 400, 400, "#888888", "▢", true),

        new("path", "paths", 80, 600, "#C8BEA0", "┅"), new("driveway", "paths", 300, 800, "#B7B1A3", "═"),
    };

    public static PlanElement? Get(string key) => All.FirstOrDefault(e => e.Key == key);

    public static IEnumerable<PlanElement> InGroup(string group) => All.Where(e => e.Group == group);

    /// <summary>Plan objects that are not structures (they can be edited with the object type picker).</summary>
    public static IEnumerable<PlanElement> Objects => All.Where(e => !e.IsStructure);

    public static (double Width, double Height) SizeOf(string key) => Get(key) is { } e ? (e.Width, e.Height) : (200, 200);
}
