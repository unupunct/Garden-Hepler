using GardenHelper.Core.Design;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Services;

namespace GardenHelper.Tests;

public class GeometryTests
{
    [Fact]
    public void Screen_world_round_trip_and_known_values()
    {
        var t = new WorldTransform(0.5, 100, 50);
        Assert.Equal(new Pt(100, 50), t.ToScreen(new Pt(0, 0)));
        Assert.Equal(new Pt(300, 150), t.ToScreen(new Pt(400, 200)));
        var back = t.ToWorld(t.ToScreen(new Pt(123.4, -56.7)));
        Assert.Equal(123.4, back.X, 9); Assert.Equal(-56.7, back.Y, 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldTransform(0));
    }

    [Fact]
    public void Zoom_keeps_the_point_under_the_cursor_fixed()
    {
        var t = new WorldTransform(0.5, 40, 30);
        var cursor = new Pt(250, 180);
        var before = t.ToWorld(cursor);
        t.ZoomAt(cursor, 2);
        Assert.Equal(1.0, t.Scale, 9);
        var after = t.ToWorld(cursor);
        Assert.Equal(before.X, after.X, 9); Assert.Equal(before.Y, after.Y, 9);
        t.ZoomAt(cursor, 1000);
        Assert.Equal(8, t.Scale);                                 // clamped
    }

    [Fact]
    public void Polygon_area_perimeter_and_distance_match_hand_calculations()
    {
        var rect = new[] { new Pt(0, 0), new Pt(400, 0), new Pt(400, 250), new Pt(0, 250) };
        Assert.Equal(100000, GeometryMath.PolygonArea(rect), 6);          // 4 m × 2.5 m = 10 m²
        Assert.Equal(1300, GeometryMath.Perimeter(rect), 6);
        var l = new[] { new Pt(0, 0), new Pt(200, 0), new Pt(200, 100), new Pt(100, 100), new Pt(100, 200), new Pt(0, 200) };
        Assert.Equal(30000, GeometryMath.PolygonArea(l), 6);              // irregular L shape
        Assert.Equal(5, GeometryMath.Distance(new Pt(0, 0), new Pt(3, 4)));
    }

    [Fact]
    public void Rotated_rectangle_corners()
    {
        var c = GeometryMath.RectCorners(0, 0, 200, 100, 90);             // rotates about centre (100,50)
        Assert.Equal(150, c[0].X, 6); Assert.Equal(-50, c[0].Y, 6);       // (-100,-50) from the centre -> (50,-100)
        Assert.Equal(150, c[1].X, 6); Assert.Equal(150, c[1].Y, 6);
        Assert.Equal(20000, GeometryMath.PolygonArea(c), 6);              // rotation keeps the area
    }

    [Fact]
    public void Overlap_detection_handles_touching_separated_and_rotated_beds()
    {
        var a = GeometryMath.RectCorners(0, 0, 100, 100, 0);
        Assert.False(GeometryMath.ConvexOverlap(a, GeometryMath.RectCorners(100, 0, 100, 100, 0)));     // touching edge is not overlap
        Assert.False(GeometryMath.ConvexOverlap(a, GeometryMath.RectCorners(300, 0, 100, 100, 0)));
        Assert.True(GeometryMath.ConvexOverlap(a, GeometryMath.RectCorners(50, 50, 100, 100, 0)));
        // A long bed rotated 45° sweeps across a neighbour that its axis-aligned box would not reach.
        var diagonal = GeometryMath.RectCorners(-150, 40, 400, 20, 45);
        Assert.True(GeometryMath.ConvexOverlap(a, diagonal));
        Assert.False(GeometryMath.ConvexOverlap(a, GeometryMath.RectCorners(300, 300, 100, 100, 45)));
    }

    [Fact]
    public void Point_in_polygon_and_boundary_containment()
    {
        var boundary = new[] { new Pt(0, 0), new Pt(1000, 0), new Pt(1000, 600), new Pt(500, 900), new Pt(0, 600) };   // house-shaped plot
        Assert.True(GeometryMath.PointInPolygon(new Pt(500, 500), boundary));
        Assert.False(GeometryMath.PointInPolygon(new Pt(900, 850), boundary));
        Assert.True(GeometryMath.PolygonInside(GeometryMath.RectCorners(100, 100, 200, 200, 0), boundary));
        Assert.False(GeometryMath.PolygonInside(GeometryMath.RectCorners(800, 700, 300, 300, 0), boundary));
    }
}

public class PlantLayoutTests
{
    [Fact]
    public void Bed_120_by_300_with_30_cm_spacing_holds_forty_plants()
    {
        var g = PlantLayout.Fit(120, 300, 30, 30, 10);
        Assert.Equal(4, g.Columns);     // rows across the width: (100/30)+1
        Assert.Equal(10, g.Rows);       // plants along the length: (280/30)+1
        Assert.Equal(40, g.Total);
        Assert.Equal(40, PlantLayout.Positions(120, 300, 30, 30, 10).Count());
    }

    [Fact]
    public void All_positions_stay_inside_the_margins_and_spacing_is_respected()
    {
        var pts = PlantLayout.Positions(120, 300, 30, 40, 10).ToList();
        Assert.All(pts, p => { Assert.InRange(p.X, 10, 110); Assert.InRange(p.Y, 10, 290); });
        var nearest = pts.SelectMany((a, i) => pts.Skip(i + 1).Select(b => GeometryMath.Distance(a, b))).Min();
        Assert.True(nearest >= 30 - 1e-9);
    }

    [Theory]
    [InlineData(15, 300, 30, 0)]        // narrower than twice the margin
    [InlineData(120, 19, 30, 0)]
    [InlineData(20, 20, 30, 1)]         // exactly the margins: one plant in the middle
    public void Very_small_beds(double w, double l, double spacing, int expected) =>
        Assert.Equal(expected, PlantLayout.Fit(w, l, spacing).Total);

    [Fact]
    public void Row_spacing_defaults_to_plant_spacing_and_rejects_nonsense()
    {
        Assert.Equal(PlantLayout.Fit(100, 100, 25, 25).Total, PlantLayout.Fit(100, 100, 25).Total);
        Assert.Throws<ArgumentOutOfRangeException>(() => PlantLayout.Fit(100, 100, 0));
    }
}

public class UndoStackTests
{
    [Fact]
    public async Task Undo_redo_replays_in_order_and_new_edits_clear_redo()
    {
        var value = 0; var stack = new UndoStack();
        void Edit(int to) { var from = value; value = to; stack.Push(new UndoEntry($"set {to}", () => { value = from; return Task.CompletedTask; }, () => { value = to; return Task.CompletedTask; })); }
        Edit(1); Edit(2); Edit(3);
        await stack.UndoAsync(); await stack.UndoAsync();
        Assert.Equal(1, value); Assert.True(stack.CanRedo); Assert.Equal("set 2", stack.RedoText);
        await stack.RedoAsync();
        Assert.Equal(2, value);
        Edit(9);
        Assert.False(stack.CanRedo);
        await stack.UndoAsync(); await stack.UndoAsync(); await stack.UndoAsync();
        Assert.Equal(0, value); Assert.False(stack.CanUndo);
        await stack.UndoAsync();                                     // no-op at the start
        Assert.Equal(0, value);
    }

    [Fact]
    public void History_is_limited()
    {
        var stack = new UndoStack { Limit = 3 };
        for (var i = 0; i < 10; i++) stack.Push(new UndoEntry("x", () => Task.CompletedTask, () => Task.CompletedTask));
        Assert.True(stack.CanUndo);
    }
}

public class RotationTests
{
    private static readonly KnowledgeBase Kb = KnowledgeBase.Load(new[] { Fixtures.RepoContent() });
    private static BedPlanting P(string plant, int? year) => new() { PlantId = plant, Status = PlantingStatus.Finished, PlantedDate = year is null ? null : new DateOnly(year.Value, 5, 1) };
    private static RotationCheck Check(string candidate, int year, params BedPlanting[] history) => RotationAdvisor.Check(Kb.Get(candidate)!, year, history, Kb);

    [Fact]
    public void Rules_file_loads_and_validates()
    {
        Assert.True(Kb.RotationRules.Count >= 7);
        Assert.Equal(3, Kb.RuleFor("Solanaceae")!.MinYears);
        Assert.Equal(4, Kb.RuleFor("Amaryllidaceae")!.MinYears);
        Assert.All(Kb.RotationRules, r => Assert.False(string.IsNullOrWhiteSpace(r.ReasonText("ro"))));
    }

    [Fact]
    public void Tomato_after_potato_one_year_later_is_a_conflict_with_an_explanation()
    {
        var c = Check("tomato", 2027, P("potato", 2026));
        Assert.Equal(RotationVerdict.Conflict, c.Verdict);
        Assert.Equal("rot_too_soon", c.Key);
        Assert.Contains("Potato", c.Args);
        Assert.False(string.IsNullOrWhiteSpace(c.RuleReason));
    }

    [Theory]
    [InlineData(2026, RotationVerdict.Conflict)]    // 2 years < 3
    [InlineData(2025, RotationVerdict.Compatible)]  // exactly 3
    [InlineData(2020, RotationVerdict.Compatible)]
    public void Gap_is_compared_with_the_family_minimum(int tomatoYear, RotationVerdict expected) =>
        Assert.Equal(expected, Check("pepper", 2028, P("tomato", tomatoYear)).Verdict);

    [Fact]
    public void Different_family_is_compatible_and_same_year_is_a_caution()
    {
        Assert.Equal(RotationVerdict.Compatible, Check("kale", 2027, P("potato", 2026)).Verdict);
        Assert.Equal(RotationVerdict.Caution, Check("radish", 2027, P("kale", 2027)).Verdict);
    }

    [Fact]
    public void Missing_information_is_reported_not_guessed()
    {
        Assert.Equal(RotationVerdict.Insufficient, Check("tomato", 2027).Verdict);                           // no history at all
        Assert.Equal(RotationVerdict.Insufficient, Check("tomato", 2027, P("potato", null)).Verdict);        // undated
        // An annual whose family has no rule (borage: Boraginaceae) cannot be judged.
        Assert.Equal(RotationVerdict.Insufficient, Check("borage", 2027, P("potato", 2026)).Verdict);
    }

    [Fact]
    public void Perennial_crops_are_not_forced_into_annual_rotation() =>
        Assert.Equal(RotationVerdict.NotApplicable, Check("apple", 2027, P("potato", 2026)).Verdict);

    [Fact]
    public void Garlic_needs_a_longer_break_than_lettuce_family()
    {
        Assert.Equal(RotationVerdict.Conflict, Check("onion", 2029, P("garlic", 2026)).Verdict);
        Assert.Equal(RotationVerdict.Compatible, Check("onion", 2030, P("garlic", 2026)).Verdict);
        Assert.Equal(RotationVerdict.Compatible, Check("lettuce", 2027, P("lettuce", 2026)).Verdict);
    }
}

public class CalendarPlannerTests
{
    private static readonly KnowledgeBase Kb = KnowledgeBase.Load(new[] { Fixtures.RepoContent() });
    private static readonly Garden G = new() { LastSpringFrost = new DateOnly(2000, 4, 25), FirstAutumnFrost = new DateOnly(2000, 10, 20) };

    [Fact]
    public void Planned_planting_produces_sowing_activities_in_the_right_months()
    {
        var bed = new GardenBed { Id = 1, Name = "A", Plantings = { new BedPlanting { PlantId = "tomato" } } };
        var all = CalendarPlanner.ForYear(G, new[] { bed }, Kb, 2027);
        var march = CalendarPlanner.ForMonth(all, 3).Where(a => a.Kind == "SowIndoors").ToList();
        Assert.NotEmpty(march);                                       // 8–6 weeks before 25 Apr = 28 Feb – 14 Mar... and into March
        Assert.Contains(CalendarPlanner.ForMonth(all, 5), a => a.Kind == "Transplant");
        Assert.All(all, a => Assert.InRange(a.Start, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31)));
    }

    [Fact]
    public void Growing_planting_produces_a_harvest_span_that_can_cross_months()
    {
        var bed = new GardenBed { Id = 1, Name = "A", Plantings = { new BedPlanting { PlantId = "radish", Status = PlantingStatus.Growing, PlantedDate = new DateOnly(2027, 5, 20) } } };
        var harvest = CalendarPlanner.ForYear(G, new[] { bed }, Kb, 2027).Where(a => a.Kind == "Harvest").ToList();
        Assert.Contains(harvest, h => h.Month == 6);                  // 25–40 days from 20 May = 14–29 Jun
        Assert.All(harvest, h => Assert.True(h.Start <= h.End));
    }

    [Fact]
    public void Without_a_frost_date_no_sowing_windows_are_invented()
    {
        var bed = new GardenBed { Id = 1, Name = "A", Plantings = { new BedPlanting { PlantId = "tomato" } } };
        Assert.Empty(CalendarPlanner.ForYear(new Garden(), new[] { bed }, Kb, 2027));
    }

    [Fact]
    public void Finished_plantings_are_ignored()
    {
        var bed = new GardenBed { Id = 1, Name = "A", Plantings = { new BedPlanting { PlantId = "tomato", Status = PlantingStatus.Finished } } };
        Assert.Empty(CalendarPlanner.ForYear(G, new[] { bed }, Kb, 2027));
    }
}
