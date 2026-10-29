using System.Windows;
using System.Windows.Media;
using GardenHelper.Core.Knowledge;

namespace GardenHelper.App.Infrastructure;

/// <summary>One plant drawn on a bed: where (cm, inside the bed), how big, and which icon and colour.</summary>
public sealed record PlantMarker(double X, double Y, double Size, string Shape, Color Body, int PlantingId = 0, string Tip = "");

/// <summary>
/// Original, simple plant icons drawn as vector shapes (24 x 24 design grid): a body in the plant's own colour and an
/// accent (leaves, stem, trunk). They are chosen from the plant's kind, so every plant in the encyclopedia gets one.
/// </summary>
public static class PlantIcons
{
    private sealed record Shape(Geometry Body, Geometry? Accent, Color AccentColor);

    private static Geometry G(string data) { var g = Geometry.Parse(data); g.Freeze(); return g; }
    private static string Circle(double cx, double cy, double r) => $"M{cx - r},{cy} a{r},{r} 0 1,0 {2 * r},0 a{r},{r} 0 1,0 {-2 * r},0 Z ";
    private static readonly Color Leaf = Color.FromRgb(0x3E, 0x8E, 0x4C), Wood = Color.FromRgb(0x7A, 0x56, 0x2E), Terracotta = Color.FromRgb(0xB8, 0x6B, 0x4A), Yellow = Color.FromRgb(0xF2, 0xC2, 0x2E);

    private static readonly Dictionary<string, Shape> Shapes = new()
    {
        ["leaf"] = new(G("M12,2 C5,7 4,14 12,22 C20,14 19,7 12,2 Z"), G("M11.4,5 L11.4,20 L12.6,20 L12.6,5 Z"), Color.FromArgb(120, 255, 255, 255)),
        ["root"] = new(G("M12,23 L7,9 Q12,5 17,9 Z"), G("M12,7 L8,1 L11,6 L12,0 L13,6 L16,1 Z"), Leaf),
        ["fruit"] = new(G("M12,22 C5,22 3,16 5,12 C7,8 17,8 19,12 C21,16 19,22 12,22 Z"), G("M12,9 L12,4 L16,6 L13,8 Z"), Leaf),
        ["tree"] = new(G("M12,1 C6,1 2,7 6,11 C3,14 6,18 12,17 C18,18 21,14 18,11 C22,7 18,1 12,1 Z"), G("M11,15 L11,23 L13,23 L13,15 Z"), Wood),
        ["flower"] = new(G(Circle(12, 5.5, 3.2) + Circle(18, 10.5, 3.2) + Circle(15.7, 17, 3.2) + Circle(8.3, 17, 3.2) + Circle(6, 10.5, 3.2)), G(Circle(12, 11.5, 3)), Yellow),
        ["bulb"] = new(G("M12,5 C6,10 4,15 7,19 C9,22 15,22 17,19 C20,15 18,10 12,5 Z"), G("M11,5 L12,0 L13,5 Z"), Leaf),
        ["pod"] = new(G("M3,16 C3,8 15,3 21,6 C21,14 11,21 3,16 Z"), G(Circle(8.5, 13.5, 1.6) + Circle(12.5, 11.2, 1.6) + Circle(16.3, 9.2, 1.6)), Color.FromArgb(150, 255, 255, 255)),
        ["grass"] = new(G("M11,23 L11,6 L12,1 L13,6 L13,23 Z"), G("M9,8 L11,6 L11,10 Z M15,8 L13,6 L13,10 Z M9,13 L11,11 L11,15 Z M15,13 L13,11 L13,15 Z"), Yellow),
        ["mushroom"] = new(G("M2.5,13 C2.5,5 21.5,5 21.5,13 Z"), G("M10,13 L10,21 L14,21 L14,13 Z"), Color.FromRgb(0xEF, 0xE6, 0xD2)),
        ["vine"] = new(G("M12,22 C12,14 6,12 6,6 C6,3 10,2 12,5 C14,2 18,3 18,6 C18,12 12,14 12,22 Z"), G("M11.4,8 L11.4,19 L12.6,19 L12.6,8 Z"), Color.FromArgb(120, 255, 255, 255)),
        ["berry"] = new(G(Circle(8, 16, 4.3) + Circle(16, 16, 4.3) + Circle(12, 9.7, 4.3)), G("M12,6 L12,1 L17,3 Z"), Leaf),
        ["houseplant"] = new(G("M12,1 C6,5 5,10 12,14 C19,10 18,5 12,1 Z"), G("M7,15 L17,15 L15,23 L9,23 Z"), Terracotta),
        ["sprout"] = new(G("M12,22 L12,12 C12,7 6,6 3,8 C4,13 9,14 12,13 C12,9 16,5 21,6 C20,12 16,14 12,16 Z"), null, Leaf),
    };

    private static readonly Dictionary<string, (string Shape, string Hex)> Specific = new()
    {
        ["tomato"] = ("fruit", "#D8402C"), ["pepper"] = ("fruit", "#D9482B"), ["hot-pepper"] = ("fruit", "#B8281E"), ["eggplant"] = ("fruit", "#6B3F8C"),
        ["potato"] = ("fruit", "#C9A25B"), ["pumpkin"] = ("fruit", "#E8872B"), ["melon"] = ("fruit", "#B9D27A"), ["watermelon"] = ("fruit", "#3F8F4F"),
        ["carrot"] = ("root", "#EE8A28"), ["radish"] = ("root", "#D94A6B"), ["beetroot"] = ("root", "#8E2A5A"), ["parsnip"] = ("root", "#E8D9A8"),
        ["turnip"] = ("root", "#D8C6E0"), ["celeriac"] = ("root", "#D8C9A0"), ["horseradish"] = ("root", "#E3D7B5"), ["jerusalem-artichoke"] = ("root", "#B58B5B"),
        ["onion"] = ("bulb", "#D9A74A"), ["garlic"] = ("bulb", "#EFE9DA"), ["leek"] = ("bulb", "#9CC48A"),
        ["pea"] = ("pod", "#6DB547"), ["bean"] = ("pod", "#5AA13C"), ["broad-bean"] = ("pod", "#7DB85A"), ["courgette"] = ("pod", "#4F8F3A"), ["cucumber"] = ("pod", "#5FA845"),
        ["sweetcorn"] = ("grass", "#E6C84A"), ["winter-rye"] = ("grass", "#C9B25A"),
        ["lettuce"] = ("leaf", "#8CCB5E"), ["cabbage"] = ("leaf", "#A9D18A"), ["kale"] = ("leaf", "#2F7A45"), ["spinach"] = ("leaf", "#2E8B4A"), ["chard"] = ("leaf", "#3D9A55"),
        ["strawberry"] = ("berry", "#D8344B"), ["blackcurrant"] = ("berry", "#3A2A5A"), ["redcurrant"] = ("berry", "#D8344B"), ["raspberry"] = ("berry", "#C2305A"),
        ["blueberry"] = ("berry", "#4A5FA8"), ["blackberry"] = ("berry", "#3B2D4F"), ["gooseberry"] = ("berry", "#A9C26A"), ["aronia"] = ("berry", "#3A2A4A"),
        ["sunflower"] = ("flower", "#F2B91E"), ["marigold"] = ("flower", "#EE8A28"), ["calendula"] = ("flower", "#F29A2E"), ["nasturtium"] = ("flower", "#E8582B"),
        ["lavender"] = ("flower", "#8C6BC2"), ["borage"] = ("flower", "#5A82D8"), ["phacelia"] = ("flower", "#9A7BD0"), ["crimson-clover"] = ("flower", "#C2354B"),
        ["red-clover"] = ("flower", "#C25A8A"), ["white-clover"] = ("flower", "#EFEFEF"), ["buckwheat"] = ("flower", "#F4E6E6"),
        ["grape"] = ("vine", "#7A3F8C"), ["hardy-kiwi"] = ("vine", "#7DB85A"),
    };

    /// <summary>The icon shape and body colour for a plant: specific for common crops, otherwise from its category.</summary>
    public static (string Shape, Color Body) For(PlantRecord? plant)
    {
        if (plant is null) return ("sprout", Leaf);
        if (Specific.TryGetValue(plant.Id, out var s)) return (s.Shape, (Color)ColorConverter.ConvertFromString(s.Hex));
        return plant.Category switch
        {
            "fruit-tree" or "nut-tree" => ("tree", Color.FromRgb(0x3E, 0x8E, 0x4C)),
            "berry" => ("berry", Color.FromRgb(0x7A, 0x3F, 0x8C)),
            "vine" => ("vine", Color.FromRgb(0x5A, 0x9A, 0x4A)),
            "flower" => ("flower", Color.FromRgb(0xE0, 0x7A, 0x9A)),
            "herb" => ("leaf", Color.FromRgb(0x5F, 0xA8, 0x6A)),
            "cover-crop" => ("grass", Color.FromRgb(0x8F, 0xB8, 0x4A)),
            "mushroom" => ("mushroom", Color.FromRgb(0xC9, 0xB4, 0x8F)),
            "houseplant" => ("houseplant", Color.FromRgb(0x4F, 0x9A, 0x5A)),
            "perennial-vegetable" => ("leaf", Color.FromRgb(0x6F, 0xA8, 0x45)),
            _ => ("leaf", Color.FromRgb(0x6F, 0xB0, 0x4A)),
        };
    }

    public static Geometry? BodyOf(string shape) => Shapes.TryGetValue(shape, out var s) ? s.Body : Shapes["sprout"].Body;
    public static Geometry? AccentOf(string shape) => Shapes.TryGetValue(shape, out var s) ? s.Accent : null;
    public static Color AccentColorOf(string shape) => Shapes.TryGetValue(shape, out var s) ? s.AccentColor : Leaf;
}

/// <summary>
/// Draws the plants of one bed. Level of detail: nothing too small to see, plain dots when small, simple coloured
/// shapes when medium, full icons (body and accent) when large. Drawing directly keeps hundreds of plants fast.
/// </summary>
public sealed class BedPlantsLayer : FrameworkElement
{
    public static readonly DependencyProperty MarkersProperty = DependencyProperty.Register(nameof(Markers), typeof(IReadOnlyList<PlantMarker>), typeof(BedPlantsLayer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(BedPlantsLayer),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<PlantMarker>? Markers { get => (IReadOnlyList<PlantMarker>?)GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }
    /// <summary>Screen pixels per centimetre of the plan.</summary>
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }

    private static readonly Dictionary<Color, SolidColorBrush> Brushes = new();
    private static SolidColorBrush Brush(Color c)
    {
        if (!Brushes.TryGetValue(c, out var b)) { b = new SolidColorBrush(c); b.Freeze(); Brushes[c] = b; }
        return b;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Markers is not { Count: > 0 } markers) return;
        var px = markers[0].Size * Zoom;                                    // on-screen size of one plant
        if (px < 2.5) return;
        var outline = new Pen(Brush(Color.FromArgb(110, 30, 40, 30)), 0.7); outline.Freeze();   // grid units: the icon is scaled when drawn
        foreach (var m in markers)
        {
            var r = m.Size / 2;
            if (px < 5) { dc.DrawEllipse(Brush(m.Body), null, new Point(m.X, m.Y), r * 0.8, r * 0.8); continue; }
            var body = PlantIcons.BodyOf(m.Shape);
            if (body is null) continue;
            // The icons are drawn on a 24 x 24 grid: scale and move to the marker's cell.
            var scale = m.Size / 24.0;
            dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, m.X - r, m.Y - r));
            dc.DrawGeometry(Brush(m.Body), px >= 10 ? outline : null, body);
            if (px >= 9 && PlantIcons.AccentOf(m.Shape) is { } accent)
                dc.DrawGeometry(Brush(PlantIcons.AccentColorOf(m.Shape)), null, accent);
            dc.Pop();
        }
    }
}
