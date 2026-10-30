using GardenHelper.Core.Design;
using Xunit;

namespace GardenHelper.Tests;

public class FitFrameTests
{
    private static Pt[] Box(double x, double y, double s) => new[] { new Pt(x, y), new Pt(x + s, y), new Pt(x + s, y + s), new Pt(x, y + s) };

    [Fact]
    public void A_bed_far_from_the_property_is_left_out_of_the_first_view()
    {
        var outline = Box(0, 0, 4000);
        var near = Box(500, 500, 100); var far = Box(90000, 90000, 100);
        var pts = FitFrame.Points(outline, near.Concat(far).ToList());
        Assert.Equal(4000, pts.Max(p => p.X));
        Assert.Contains(new Pt(600, 600), pts);
    }

    [Fact]
    public void Beds_just_outside_the_outline_are_still_framed()
    {
        var pts = FitFrame.Points(Box(0, 0, 4000), Box(4500, 100, 100));
        Assert.Contains(new Pt(4600, 200), pts);
    }

    [Fact]
    public void Without_an_outline_a_few_stray_beds_among_many_are_trimmed_but_a_small_garden_is_kept_whole()
    {
        var many = Enumerable.Range(0, 100).SelectMany(i => Box((i % 10) * 200, (i / 10) * 200, 100)).ToList();
        many.AddRange(Box(500000, 500000, 100));
        Assert.True(FitFrame.Points(Array.Empty<Pt>(), many).Max(p => p.X) < 5000);
        var small = Box(0, 0, 100).Concat(Box(500000, 0, 100)).ToList();
        Assert.Equal(small.Count, FitFrame.Points(Array.Empty<Pt>(), small).Count);
    }
}
