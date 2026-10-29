using GardenHelper.Core.Design;
using GardenHelper.Core.Site;
using Xunit;

namespace GardenHelper.Tests;

public class InteriorTests
{
    [Fact]
    public void Wall_between_two_points_covers_the_line()
    {
        var w = InteriorCatalog.WallBetween(1, 0, 100, 100, 100, 400);
        Assert.Equal(300, w.Width, 3);
        Assert.Equal(90, w.Rotation, 1);
        // centre of the rectangle is the middle of the line
        Assert.Equal(100, w.X + w.Width / 2, 3); Assert.Equal(250, w.Y + w.Height / 2, 3);
    }

    [Fact]
    public void Window_snaps_onto_nearest_wall_and_takes_its_angle()
    {
        var wall = InteriorCatalog.WallBetween(1, 0, 0, 100, 400, 100);
        var win = new InteriorItem { FeatureId = 1, Kind = "window", Width = 120, Height = 12, X = 150, Y = 120 };
        Assert.True(InteriorCatalog.SnapToWall(win, new[] { wall }));
        Assert.Equal(0, win.Rotation, 1);
        Assert.Equal(100, win.Y + win.Height / 2, 1);
    }

    [Fact]
    public void Window_far_from_every_wall_stays_put()
    {
        var wall = InteriorCatalog.WallBetween(1, 0, 0, 100, 400, 100);
        var win = new InteriorItem { FeatureId = 1, Kind = "window", Width = 120, Height = 12, X = 150, Y = 400 };
        Assert.False(InteriorCatalog.SnapToWall(win, new[] { wall }));
        Assert.Equal(400, win.Y);
    }

    [Fact]
    public void Footprint_and_outline_match_a_ten_by_eight_metre_house()
    {
        var o = new GeoPoint(45.0, 25.0);
        var proj = new LocalProjection(o);
        var pts = new[] { new Pt(0, 0), new Pt(10, 0), new Pt(10, 8), new Pt(0, 8) }.Select(proj.FromMetres).ToList();
        var (w, h) = InteriorCatalog.Footprint(pts);
        Assert.InRange(w, 1090, 1110); Assert.InRange(h, 890, 910);
        var outline = InteriorCatalog.OutlineCm(pts);
        Assert.Equal(4, outline.Count);
        Assert.Equal(50, outline.Min(p => p.X), 0); Assert.Equal(50, outline.Min(p => p.Y), 0);
    }

    [Fact]
    public void Every_kind_has_a_layer_and_size()
    {
        Assert.All(InteriorCatalog.All, k => { Assert.True(k.Width > 0 && k.Height > 0); Assert.Contains(k.Group, InteriorCatalog.Groups); });
    }
}
