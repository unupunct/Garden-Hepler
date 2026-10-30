namespace GardenHelper.Core.Design;

/// <summary>Which points the first view of the plan should frame.</summary>
public static class FitFrame
{
    /// <summary>
    /// The points the first view should frame: the property outline, and the beds near it. A bed far from the property (a typing slip, or a leftover from
    /// another place) would otherwise shrink everything to a speck, so such outliers stay out of the frame (they are one click away with zoom out).
    /// </summary>
    public static List<Pt> Points(IReadOnlyList<Pt> outline, IReadOnlyList<Pt> bedCorners)
    {
        if (outline.Count > 0)
        {
            double x0 = outline.Min(p => p.X), x1 = outline.Max(p => p.X), y0 = outline.Min(p => p.Y), y1 = outline.Max(p => p.Y);
            var pad = Math.Max(x1 - x0, y1 - y0) * 0.5;
            return outline.Concat(bedCorners.Where(p => p.X >= x0 - pad && p.X <= x1 + pad && p.Y >= y0 - pad && p.Y <= y1 + pad)).ToList();
        }
        if (bedCorners.Count < 50) return bedCorners.ToList();
        var xs = bedCorners.Select(p => p.X).OrderBy(v => v).ToList(); var ys = bedCorners.Select(p => p.Y).OrderBy(v => v).ToList();
        var lo = (int)(bedCorners.Count * 0.02); var hi = bedCorners.Count - 1 - lo;
        double ax = xs[lo], bx = xs[hi], ay = ys[lo], by = ys[hi];
        return bedCorners.Where(p => p.X >= ax && p.X <= bx && p.Y >= ay && p.Y <= by).ToList();
    }
}
