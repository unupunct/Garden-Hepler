namespace GardenHelper.Core.Design;

/// <summary>A point in garden (world) space. Units are centimetres on a flat local plane, NOT latitude/longitude.</summary>
public readonly record struct Pt(double X, double Y);

/// <summary>
/// Maps between garden space (cm) and screen pixels. Scale is pixels per cm; Offset is where the world origin lands on screen.
/// Kept free of UI types so it can be tested with known values.
/// </summary>
public sealed class WorldTransform
{
    public double Scale { get; private set; } = 0.5;
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }

    public WorldTransform(double scale = 0.5, double offsetX = 0, double offsetY = 0)
    {
        if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        Scale = scale; OffsetX = offsetX; OffsetY = offsetY;
    }

    public Pt ToScreen(Pt world) => new(world.X * Scale + OffsetX, world.Y * Scale + OffsetY);
    public Pt ToWorld(Pt screen) => new((screen.X - OffsetX) / Scale, (screen.Y - OffsetY) / Scale);

    /// <summary>Zooms about a screen point so the world point under it stays put.</summary>
    public void ZoomAt(Pt screenPoint, double factor, double min = 0.02, double max = 8)
    {
        var anchor = ToWorld(screenPoint);
        Scale = Math.Clamp(Scale * factor, min, max);
        OffsetX = screenPoint.X - anchor.X * Scale;
        OffsetY = screenPoint.Y - anchor.Y * Scale;
    }

    public void Pan(double dxPx, double dyPx) { OffsetX += dxPx; OffsetY += dyPx; }
}

public static class GeometryMath
{
    public static double Distance(Pt a, Pt b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    public static double Perimeter(IReadOnlyList<Pt> poly, bool closed = true)
    {
        double sum = 0;
        for (var i = 0; i < poly.Count - 1; i++) sum += Distance(poly[i], poly[i + 1]);
        if (closed && poly.Count > 2) sum += Distance(poly[^1], poly[0]);
        return sum;
    }

    /// <summary>Planar polygon area (shoelace). Valid for local metric coordinates only.</summary>
    public static double PolygonArea(IReadOnlyList<Pt> poly)
    {
        double s = 0;
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            s += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(s) / 2;
    }

    /// <summary>Corners (clockwise from top-left) of a rectangle rotated clockwise about its centre.</summary>
    public static Pt[] RectCorners(double x, double y, double w, double h, double rotationDeg)
    {
        var cx = x + w / 2; var cy = y + h / 2;
        var r = rotationDeg * Math.PI / 180; var cos = Math.Cos(r); var sin = Math.Sin(r);
        Pt Rot(double px, double py) => new(cx + (px - cx) * cos - (py - cy) * sin, cy + (px - cx) * sin + (py - cy) * cos);
        return new[] { Rot(x, y), Rot(x + w, y), Rot(x + w, y + h), Rot(x, y + h) };
    }

    public static bool PointInPolygon(Pt p, IReadOnlyList<Pt> poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }

    /// <summary>True if two convex polygons overlap by more than touching (separating-axis test).</summary>
    public static bool ConvexOverlap(IReadOnlyList<Pt> a, IReadOnlyList<Pt> b, double tolerance = 0.01)
    {
        foreach (var poly in new[] { a, b })
            for (var i = 0; i < poly.Count; i++)
            {
                var p1 = poly[i]; var p2 = poly[(i + 1) % poly.Count];
                var ax = -(p2.Y - p1.Y); var ay = p2.X - p1.X;     // edge normal
                var len = Math.Sqrt(ax * ax + ay * ay); if (len == 0) continue;
                ax /= len; ay /= len;
                Project(a, ax, ay, out var minA, out var maxA);
                Project(b, ax, ay, out var minB, out var maxB);
                if (maxA <= minB + tolerance || maxB <= minA + tolerance) return false;
            }
        return true;
    }

    private static void Project(IReadOnlyList<Pt> poly, double ax, double ay, out double min, out double max)
    {
        min = double.MaxValue; max = double.MinValue;
        foreach (var p in poly) { var d = p.X * ax + p.Y * ay; min = Math.Min(min, d); max = Math.Max(max, d); }
    }

    /// <summary>
    /// True if a rectangle (any rotation) overlaps an arbitrary, possibly concave polygon: a corner inside it,
    /// a polygon vertex inside the rectangle, or crossing edges.
    /// </summary>
    public static bool RectOverlapsPolygon(IReadOnlyList<Pt> rect, IReadOnlyList<Pt> poly)
    {
        if (rect.Any(p => PointInPolygon(p, poly)) || poly.Any(p => PointInPolygon(p, rect))) return true;
        for (var i = 0; i < rect.Count; i++)
            for (var j = 0; j < poly.Count; j++)
                if (SegmentsCross(rect[i], rect[(i + 1) % rect.Count], poly[j], poly[(j + 1) % poly.Count])) return true;
        return false;
    }

    public static bool SegmentsCross(Pt a, Pt b, Pt c, Pt d)
    {
        static double Cross(Pt o, Pt p, Pt q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        var d1 = Cross(c, d, a); var d2 = Cross(c, d, b); var d3 = Cross(a, b, c); var d4 = Cross(a, b, d);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    public static bool PolygonInside(IReadOnlyList<Pt> inner, IReadOnlyList<Pt> boundary) => inner.All(p => PointInPolygon(p, boundary));
}

public sealed record PlantGrid(int Columns, int Rows, int Total, double UsableWidth, double UsableHeight);

public static class PlantLayout
{
    /// <summary>
    /// How many plants fit in a rectangular bed: plant centres sit on a grid, at least <paramref name="marginCm"/> from every edge.
    /// <paramref name="spacingCm"/> is the distance along a row, <paramref name="rowSpacingCm"/> between rows.
    /// </summary>
    public static PlantGrid Fit(double widthCm, double lengthCm, double spacingCm, double? rowSpacingCm = null, double marginCm = 10)
    {
        if (spacingCm <= 0) throw new ArgumentOutOfRangeException(nameof(spacingCm));
        var rowGap = rowSpacingCm is > 0 ? rowSpacingCm.Value : spacingCm;
        var uw = Math.Max(0, widthCm - 2 * marginCm); var uh = Math.Max(0, lengthCm - 2 * marginCm);
        if (widthCm < 2 * marginCm || lengthCm < 2 * marginCm) return new PlantGrid(0, 0, 0, uw, uh);
        // Rows run along the bed's length; plants within a row follow the spacing, rows follow the row gap.
        var perRow = (int)Math.Floor(uh / spacingCm + 1e-9) + 1;
        var rows = (int)Math.Floor(uw / rowGap + 1e-9) + 1;
        return new PlantGrid(rows, perRow, rows * perRow, uw, uh);
    }

    /// <summary>Plant centres (cm, relative to the bed's top-left corner) for the grid returned by <see cref="Fit"/>.</summary>
    public static IEnumerable<Pt> Positions(double widthCm, double lengthCm, double spacingCm, double? rowSpacingCm = null, double marginCm = 10)
    {
        var g = Fit(widthCm, lengthCm, spacingCm, rowSpacingCm, marginCm);
        var rowGap = rowSpacingCm is > 0 ? rowSpacingCm.Value : spacingCm;
        // Centre the grid inside the usable area.
        var gridW = (g.Columns - 1) * rowGap; var gridH = (g.Rows - 1) * spacingCm;
        var x0 = marginCm + (g.UsableWidth - gridW) / 2; var y0 = marginCm + (g.UsableHeight - gridH) / 2;
        for (var r = 0; r < g.Columns; r++)
            for (var c = 0; c < g.Rows; c++)
                yield return new Pt(x0 + r * rowGap, y0 + c * spacingCm);
    }
}
