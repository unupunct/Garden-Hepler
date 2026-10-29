using GardenHelper.Core.Design;
using GardenHelper.Core.Site;

namespace GardenHelper.Tests;

public class FrameTests
{
    private static readonly GeoPoint Cluj = new(46.7712, 23.6236);
    private static List<GeoPoint> Pts(params (double e, double n)[] m)
    {
        var proj = new LocalProjection(Cluj);
        return m.Select(x => proj.FromMetres(new Pt(x.e, x.n))).ToList();
    }

    [Fact]
    public void Plan_and_map_coordinates_convert_both_ways()
    {
        var frame = new PlanFrame(Cluj, 5000, 3000);
        var geo = new GeoPoint(46.7715, 23.6240);
        var back = frame.ToGeo(frame.ToPlan(geo));
        Assert.Equal(geo.Lat, back.Lat, 9); Assert.Equal(geo.Lon, back.Lon, 9);
        var origin = frame.ToPlan(Cluj);
        Assert.Equal(5000, origin.X, 6); Assert.Equal(3000, origin.Y, 6);
        var plan = new Pt(6234, 2100);
        var again = frame.ToPlan(frame.ToGeo(plan));
        Assert.Equal(plan.X, again.X, 6); Assert.Equal(plan.Y, again.Y, 6);
    }

    [Fact]
    public void North_is_up_and_east_is_right_at_true_scale()
    {
        var frame = new PlanFrame(Cluj, 0, 0);
        var p = frame.ToPlan(new LocalProjection(Cluj).FromMetres(new Pt(10, 20)));     // 10 m east, 20 m north
        Assert.Equal(1000, p.X, 3); Assert.Equal(-2000, p.Y, 3);
    }

    [Fact]
    public void Fit_holds_every_point_inside_the_margin_and_reports_the_size()
    {
        var pins = Pts((0, 0), (80, 0), (80, 50), (0, 50));
        var frame = PlanFrame.Fit(pins, 500, out var w, out var h);
        var plan = pins.Select(frame.ToPlan).ToList();
        Assert.Equal(500, plan.Min(p => p.X), 3); Assert.Equal(500, plan.Min(p => p.Y), 3);
        Assert.InRange(w, 8990, 9010); Assert.InRange(h, 5990, 6010);
        Assert.InRange(plan.Max(p => p.X), w - 505, w - 495);
    }

    [Fact]
    public void A_structure_drawn_in_the_designer_survives_a_round_trip_to_map_coordinates()
    {
        var frame = PlanFrame.Fit(Pts((0, 0), (60, 40)), 500, out _, out _);
        var house = GeometryMath.RectCorners(1200, 900, 1000, 800, 30);
        var geo = house.Select(frame.ToGeo).ToList();
        var back = geo.Select(frame.ToPlan).ToList();
        for (var i = 0; i < 4; i++) { Assert.Equal(house[i].X, back[i].X, 5); Assert.Equal(house[i].Y, back[i].Y, 5); }
        Assert.InRange(GeoMath.Measure(geo).AreaM2, 79.8, 80.2);          // 10 m x 8 m
    }

    [Fact]
    public void Tile_grid_covers_the_area_with_the_sharpest_zoom_that_fits()
    {
        var pins = Pts((0, 0), (120, 0), (120, 80), (0, 80));
        var grid = TileGrid.ForBounds(pins, 0.2, 5);
        Assert.InRange(grid.TilesX, 1, 5); Assert.InRange(grid.TilesY, 1, 5);
        Assert.InRange(grid.Zoom, 17, 18);
        // Every pin lies inside the stitched picture.
        foreach (var p in pins)
        {
            var (px, py) = GeoMath.ToWorldPixel(p, grid.Zoom);
            Assert.InRange(px - grid.MinX * 256.0, 0, grid.PixelWidth); Assert.InRange(py - grid.MinY * 256.0, 0, grid.PixelHeight);
        }
        // A 120 x 80 m plot needs only a few tiles even at the sharpest zoom.
        Assert.Equal(18, grid.Zoom);       // capped: sharper zoom levels often have no imagery
    }

    [Fact]
    public void A_sharper_zoom_would_not_have_fitted_in_the_tile_limit()
    {
        var pins = Pts((0, 0), (1200, 0), (1200, 800), (0, 800));
        var grid = TileGrid.ForBounds(pins, 0.2, 5);
        var tighter = TileGrid.ForBounds(pins, 0.2, 5, maxZoom: grid.Zoom + 1, minZoom: grid.Zoom + 1);   // forced one level sharper
        Assert.True(tighter.TilesX > 5 || tighter.TilesY > 5);
        Assert.True(grid.TilesX <= 5 && grid.TilesY <= 5);
    }

    [Fact]
    public void Big_areas_use_a_lower_zoom_and_tiny_ones_are_not_blown_up()
    {
        var big = TileGrid.ForBounds(Pts((0, 0), (5000, 0), (5000, 3000), (0, 3000)), 0.2, 5);
        var small = TileGrid.ForBounds(Pts((0, 0), (10, 8)), 0.2, 5);
        Assert.True(big.Zoom < small.Zoom);
        Assert.Equal(18, small.Zoom);
        Assert.InRange(small.PixelWidth * small.CmPerPixel / 100, 40, 700);   // a few dozen metres across, not a single building
        Assert.True(big.CmPerPixel > small.CmPerPixel);
    }
}
