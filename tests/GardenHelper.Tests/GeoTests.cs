using GardenHelper.Core.Design;
using GardenHelper.Core.Site;

namespace GardenHelper.Tests;

public class GeoTests
{
    private static readonly GeoPoint Cluj = new(46.7712, 23.6236);

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(46.7712, 23.6236, 18)]
    [InlineData(-33.9, 151.2, 12)]
    [InlineData(45.9432, 24.9668, 7)]
    public void World_pixel_round_trip(double lat, double lon, int zoom)
    {
        var (x, y) = GeoMath.ToWorldPixel(new GeoPoint(lat, lon), zoom);
        var back = GeoMath.FromWorldPixel(x, y, zoom);
        Assert.Equal(lat, back.Lat, 7); Assert.Equal(lon, back.Lon, 7);
    }

    [Fact]
    public void Equator_zero_zoom_maps_to_the_tile_centre()
    {
        var (x, y) = GeoMath.ToWorldPixel(new GeoPoint(0, 0), 0);
        Assert.Equal(128, x, 6); Assert.Equal(128, y, 6);
    }

    [Fact]
    public void Local_projection_known_distances()
    {
        var proj = new LocalProjection(new GeoPoint(0, 0));
        var north = proj.ToMetres(new GeoPoint(1, 0));
        Assert.InRange(north.Y, 111190, 111200);           // 1° of latitude ≈ 111.2 km
        var back = proj.FromMetres(new Pt(250, -400));
        var again = proj.ToMetres(back);
        Assert.Equal(250, again.X, 6); Assert.Equal(-400, again.Y, 6);
    }

    [Fact]
    public void A_100_m_square_in_Romania_measures_10000_square_metres()
    {
        var proj = new LocalProjection(Cluj);
        var square = new[] { new Pt(0, 0), new Pt(100, 0), new Pt(100, 100), new Pt(0, 100) }.Select(proj.FromMetres).ToList();
        var (area, perimeter) = GeoMath.Measure(square);
        Assert.InRange(area, 9995, 10005);                 // within 0.05 %
        Assert.Equal(400, perimeter, 0);
    }

    [Fact]
    public void Irregular_twenty_pin_outline_is_measured_and_accepted()
    {
        // A 60 x 40 m rectangle with a pin every 10 m along its 200 m perimeter: exactly 20 pins, corners included.
        var proj = new LocalProjection(Cluj);
        var pts = new List<Pt>();
        for (var i = 0; i < 6; i++) pts.Add(new Pt(i * 10, 0));
        for (var i = 0; i < 4; i++) pts.Add(new Pt(60, i * 10));
        for (var i = 0; i < 6; i++) pts.Add(new Pt(60 - i * 10, 40));
        for (var i = 0; i < 4; i++) pts.Add(new Pt(0, 40 - i * 10));
        var pins = pts.Select(proj.FromMetres).ToList();
        Assert.Equal(20, pins.Count);
        Assert.InRange(GeoMath.Measure(pins).AreaM2, 2398, 2402);
        Assert.False(GeoMath.SelfIntersects(pins));
    }

    [Fact]
    public void Fewer_than_three_pins_have_no_area()
    {
        Assert.Equal((0.0, 0.0), GeoMath.Measure(new[] { Cluj, new GeoPoint(46.7713, 23.6237) }));
        Assert.Equal((0.0, 0.0), GeoMath.Measure(Array.Empty<GeoPoint>()));
    }

    [Fact]
    public void Bow_tie_outline_is_detected()
    {
        var proj = new LocalProjection(Cluj);
        var bow = new[] { new Pt(0, 0), new Pt(50, 50), new Pt(50, 0), new Pt(0, 50) }.Select(proj.FromMetres).ToList();
        Assert.True(GeoMath.SelfIntersects(bow));
        var ok = new[] { new Pt(0, 0), new Pt(50, 0), new Pt(50, 50), new Pt(0, 50) }.Select(proj.FromMetres).ToList();
        Assert.False(GeoMath.SelfIntersects(ok));
    }

    [Fact]
    public void Boundary_is_placed_on_the_plan_at_true_scale_with_north_up()
    {
        var proj = new LocalProjection(Cluj);
        var pins = new[] { new Pt(-20, -10), new Pt(20, -10), new Pt(20, 10), new Pt(-20, 10) }.Select(proj.FromMetres).ToList();
        var plan = GeoMath.ToPlan(pins, Cluj, 5000, 4000);               // origin at the plan point (5000, 4000) cm
        Assert.Equal(3000, plan[0].X, 3); Assert.Equal(4000 + 1000, plan[0].Y, 3);   // west, south of origin: y grows downwards
        Assert.Equal(7000, plan[2].X, 3); Assert.Equal(4000 - 1000, plan[2].Y, 3);
        var expectedCm2 = GeoMath.Measure(pins).AreaM2 * 10000;           // m² -> cm²
        Assert.InRange(GeometryMath.PolygonArea(plan) / expectedCm2, 0.999, 1.001);
    }

    [Fact]
    public void Fitted_plan_starts_at_the_margin_and_sizes_the_canvas()
    {
        var proj = new LocalProjection(Cluj);
        var pins = new[] { new Pt(0, 0), new Pt(30, 0), new Pt(30, 20), new Pt(0, 20) }.Select(proj.FromMetres).ToList();
        var (pts, w, h) = GeoMath.ToPlanFitted(pins, 500);
        Assert.Equal(500, pts.Min(p => p.X), 3); Assert.Equal(500, pts.Min(p => p.Y), 3);
        Assert.InRange(w, 3990, 4010); Assert.InRange(h, 2990, 3010);     // 30 m x 20 m plus margins
    }

    [Fact]
    public void Outline_survives_json_round_trip_and_bad_json_is_ignored()
    {
        var pins = new List<GeoPoint> { Cluj, new(46.7713, 23.6237), new(46.7714, 23.6235) };
        var back = GeoMath.FromJson(GeoMath.ToJson(pins));
        Assert.Equal(3, back.Count); Assert.Equal(pins[1], back[1]);
        Assert.Empty(GeoMath.FromJson("{ not json"));
        Assert.Empty(GeoMath.FromJson(null));
    }

    [Fact]
    public void Designer_boundary_check_flags_beds_outside_the_property()
    {
        var proj = new LocalProjection(Cluj);
        var pins = new[] { new Pt(0, 0), new Pt(40, 0), new Pt(40, 25), new Pt(0, 25) }.Select(proj.FromMetres).ToList();
        var (plot, _, _) = GeoMath.ToPlanFitted(pins, 0);                // 4000 x 2500 cm, origin at 0,0
        Assert.True(GeometryMath.PolygonInside(GeometryMath.RectCorners(200, 200, 120, 400, 0), plot));
        Assert.False(GeometryMath.PolygonInside(GeometryMath.RectCorners(3900, 200, 120, 400, 0), plot));
    }
}
