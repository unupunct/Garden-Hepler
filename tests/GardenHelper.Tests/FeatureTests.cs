using GardenHelper.Core.Design;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Services;
using GardenHelper.Core.Site;
using Microsoft.EntityFrameworkCore;

namespace GardenHelper.Tests;

public class FeatureTests
{
    private static readonly GeoPoint Cluj = new(46.7712, 23.6236);

    private static List<GeoPoint> Rect(double eastM, double northM, double w, double h)
    {
        var proj = new LocalProjection(Cluj);
        return new[] { new Pt(eastM, northM), new Pt(eastM + w, northM), new Pt(eastM + w, northM + h), new Pt(eastM, northM + h) }.Select(proj.FromMetres).ToList();
    }

    [Fact]
    public void Catalog_separates_structures_from_garden_spaces()
    {
        Assert.True(FeatureCatalog.IsStructure("house")); Assert.True(FeatureCatalog.IsStructure("workshop"));
        Assert.False(FeatureCatalog.IsStructure("garden")); Assert.False(FeatureCatalog.IsStructure("compost"));
        Assert.Empty(FeatureCatalog.Structures.Intersect(FeatureCatalog.Spaces));
    }

    [Fact]
    public void Several_shapes_keep_their_relative_positions_on_one_plane()
    {
        var outline = Rect(0, 0, 100, 60);
        var house = Rect(10, 10, 12, 8);
        var (shapes, w, h) = GeoMath.ToPlanFittedMany(new IReadOnlyList<GeoPoint>[] { outline, house }, 500);
        Assert.Equal(500, shapes.SelectMany(s => s).Min(p => p.X), 3);
        // The house sits 10 m east and 10 m north of the outline's south-west corner, with y pointing down.
        var swOutline = shapes[0].OrderByDescending(p => p.Y).ThenBy(p => p.X).First();
        var swHouse = shapes[1].OrderByDescending(p => p.Y).ThenBy(p => p.X).First();
        Assert.InRange(swHouse.X - swOutline.X, 995, 1005);
        Assert.InRange(swOutline.Y - swHouse.Y, 995, 1005);
        Assert.InRange(GeometryMath.PolygonArea(shapes[1]), 12 * 8 * 10000 * 0.998, 12 * 8 * 10000 * 1.002);
        Assert.InRange(w, 10000 + 900, 10000 + 1100);
        Assert.InRange(h, 6000 + 900, 6000 + 1100);
        Assert.Empty(GeoMath.ToPlanFittedMany(Array.Empty<IReadOnlyList<GeoPoint>>()).Shapes);
    }

    [Fact]
    public void Rectangle_polygon_overlap_handles_concave_structures_and_rotation()
    {
        // An L-shaped house: the notch is empty ground.
        var l = new[] { new Pt(0, 0), new Pt(400, 0), new Pt(400, 200), new Pt(200, 200), new Pt(200, 400), new Pt(0, 400) };
        Assert.True(GeometryMath.RectOverlapsPolygon(GeometryMath.RectCorners(50, 50, 100, 100, 0), l));          // inside the house
        Assert.False(GeometryMath.RectOverlapsPolygon(GeometryMath.RectCorners(250, 250, 100, 100, 0), l));       // in the notch
        Assert.True(GeometryMath.RectOverlapsPolygon(GeometryMath.RectCorners(150, 250, 100, 100, 0), l));        // straddles the inner edge
        Assert.True(GeometryMath.RectOverlapsPolygon(GeometryMath.RectCorners(-50, 150, 600, 40, 0), l));         // crosses with no corner inside
        Assert.False(GeometryMath.RectOverlapsPolygon(GeometryMath.RectCorners(600, 600, 100, 100, 30), l));
    }

    [Fact]
    public async Task Features_round_trip_and_deleting_one_removes_its_log()
    {
        using var env = await TestEnv.CreateAsync();
        var repo = new Repository(env.Factory);
        var f = await repo.SaveAsync(new PropertyFeature { GardenId = 1, Name = "Garden 1", Kind = "garden", PointsJson = GeoMath.ToJson(Rect(0, 0, 10, 10)) });
        await repo.SaveAsync(new FeatureLog { FeatureId = f.Id, Kind = "soil", Text = "Added compost 5 cm" });
        Assert.Equal(4, GeoMath.FromJson((await repo.ListAsync<PropertyFeature>()).Single().PointsJson).Count);
        await repo.DeleteAsync(f);
        Assert.Empty(await repo.ListAsync<FeatureLog>());
    }

    [Fact]
    public async Task Places_are_found_by_name_kind_notes_and_log_text()
    {
        using var env = await TestEnv.CreateAsync();
        var repo = new Repository(env.Factory);
        var a = await repo.SaveAsync(new PropertyFeature { GardenId = 1, Name = "Compost pile", Kind = "compost", Notes = "Turn monthly" });
        var b = await repo.SaveAsync(new PropertyFeature { GardenId = 1, Name = "Garden 1", Kind = "garden" });
        await repo.SaveAsync(new FeatureLog { FeatureId = b.Id, Kind = "plant", Text = "Sprayed nettle tea against aphids" });
        var search = new GlobalSearchService(KnowledgeBase.Load(new[] { Fixtures.RepoContent() }), env.Library, repo);

        Assert.Contains(await search.SearchAsync("compost", "en"), r => r.Kind == SearchKind.Feature && r.TargetId == a.Id.ToString());
        Assert.Contains(await search.SearchAsync("garden 1", "en"), r => r.Kind == SearchKind.Feature && r.TargetId == b.Id.ToString());
        Assert.Contains(await search.SearchAsync("turn monthly", "en"), r => r.Kind == SearchKind.Feature);
        Assert.Contains(await search.SearchAsync("aphids", "en"), r => r.Kind == SearchKind.Feature && r.TargetId == b.Id.ToString());
        Assert.DoesNotContain(await search.SearchAsync("greenhouse", "en"), r => r.Kind == SearchKind.Feature);
    }

    [Fact]
    public async Task Bed_can_be_linked_to_a_feature_and_survives_feature_lookup()
    {
        using var env = await TestEnv.CreateAsync();
        var repo = new Repository(env.Factory);
        var f = await repo.SaveAsync(new PropertyFeature { GardenId = 1, Name = "Garden 1" });
        await repo.SaveAsync(new GardenBed { GardenId = 1, Name = "Garden 1", FeatureId = f.Id });
        var beds = await repo.ListAsync<GardenBed>(q => q.Where(b => b.FeatureId == f.Id));
        Assert.Single(beds);
    }
}
