using GardenHelper.Core.Domain;
using GardenHelper.Core.Planning;
using Xunit;

namespace GardenHelper.Tests;

public class TreeTests
{
    [Fact]
    public void Distance_is_zero_inside_and_measured_to_the_edge_outside()
    {
        Assert.Equal(0, TreeRules.DistanceToRect(50, 50, 0, 0, 100, 100, 0));
        Assert.Equal(30, TreeRules.DistanceToRect(130, 50, 0, 0, 100, 100, 0), 6);
        Assert.Equal(5, TreeRules.DistanceToRect(105, 50, 0, 0, 100, 100, 90), 6);   // square: rotation changes nothing
    }

    [Fact]
    public void Rotation_is_taken_into_account()
    {
        // A 200 x 20 bed centred on (100, 10), turned 90 degrees, now stands upright: x in 90..110, y in -90..110.
        Assert.Equal(0, TreeRules.DistanceToRect(100, 100, 0, 0, 200, 20, 90), 6);
        Assert.Equal(40, TreeRules.DistanceToRect(150, 100, 0, 0, 200, 20, 90), 6);
        Assert.True(TreeRules.DistanceToRect(190, 10, 0, 0, 200, 20, 90) > 50);       // far end of the unrotated bed is now empty
    }

    [Fact]
    public void Shade_is_full_half_or_none()
    {
        Assert.Equal(100, TreeRules.ShadePercent(50, 50, 500, 0, 0, 100, 100, 0));
        Assert.Equal(0, TreeRules.ShadePercent(1000, 1000, 100, 0, 0, 100, 100, 0));
        var half = TreeRules.ShadePercent(0, 50, 400, 0, 0, 100, 100, 0);              // crown edge far past the bed: whole bed
        Assert.Equal(100, half);
        Assert.InRange(TreeRules.ShadePercent(150, 50, 100, 0, 0, 100, 100, 0), 20, 55);   // crown edge cuts into the bed's near side
    }

    [Fact]
    public void Roots_reach_further_than_shade_and_results_are_sorted()
    {
        var beds = new[]
        {
            new GardenBed { Id = 1, Name = "Far", X = 1000, Y = 0, Width = 100, Height = 100 },
            new GardenBed { Id = 2, Name = "Roots only", X = 350, Y = -50, Width = 100, Height = 100 },   // 150 cm from a tree at 200,0 with crown 100: no shade, root 150
            new GardenBed { Id = 3, Name = "Under crown", X = 150, Y = -50, Width = 100, Height = 100 },
        };
        var r = TreeRules.Interactions(200, 0, 100, null, beds);
        Assert.Equal(new[] { 3, 2 }, r.Select(i => i.BedId));
        Assert.True(r[0].ShadePercent > 0 && r[0].RootsReach);
        Assert.Equal(0, r[1].ShadePercent);
        Assert.True(r[1].RootsReach);
        Assert.Equal(200, TreeRules.RootRadius(100, 200));
        Assert.Equal(150, TreeRules.RootRadius(100, null));
    }

    [Fact]
    public void Seasonal_actions_depend_on_type_month_and_age()
    {
        Assert.Contains("prune_dormant", TreeRules.SeasonalActions("fruit", 2, 10));
        Assert.Contains("dormant_spray", TreeRules.SeasonalActions("fruit", 2, 10));
        Assert.DoesNotContain("dormant_spray", TreeRules.SeasonalActions("nut", 2, 10));
        Assert.DoesNotContain("protect_young", TreeRules.SeasonalActions("fruit", 11, 10));
        Assert.Contains("protect_young", TreeRules.SeasonalActions("fruit", 11, 1));
        Assert.Contains("deep_water", TreeRules.SeasonalActions("unknown-type", 7, null));
        Assert.Equal(new DateOnly(2026, 6, 1).DayNumber - new DateOnly(2024, 6, 1).DayNumber, (int)Math.Round(TreeRules.AgeYears(new DateOnly(2024, 6, 1), new DateOnly(2026, 6, 1))!.Value * 365.25));
    }
}
