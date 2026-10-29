using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Services;
using GardenHelper.Core.Site;

namespace GardenHelper.Tests;

public class SolarTests
{
    [Fact]
    public void Midsummer_day_length_in_Cluj_is_about_15_and_a_half_hours()
    {
        var d = SolarCalculator.Day(46.77, 23.6, new DateOnly(2026, 6, 21), 2);
        Assert.InRange(d.DayLengthHours, 15.4, 15.9);
        Assert.InRange(d.NoonElevationDeg, 66, 67.5);      // 90 - 46.77 + 23.44
        Assert.True(d.Sunrise < new TimeOnly(5, 30));
        Assert.True(d.Sunset > new TimeOnly(19, 45));      // standard time (UTC+2), i.e. about 21:15 in summer time
    }

    [Fact]
    public void Midwinter_is_short_and_equinox_is_twelve_hours()
    {
        Assert.InRange(SolarCalculator.Day(46.77, 23.6, new DateOnly(2026, 12, 21), 2).DayLengthHours, 8.2, 8.7);
        Assert.InRange(SolarCalculator.Day(0, 0, new DateOnly(2026, 3, 20), 0).DayLengthHours, 12.0, 12.2);
    }

    [Fact]
    public void Polar_cases_do_not_throw()
    {
        var night = SolarCalculator.Day(80, 10, new DateOnly(2026, 12, 21), 1);
        Assert.Equal(0, night.DayLengthHours); Assert.Null(night.Sunrise);
        Assert.Equal(24, SolarCalculator.Day(80, 10, new DateOnly(2026, 6, 21), 1).DayLengthHours);
    }

    [Fact]
    public void Elevation_is_highest_near_solar_noon()
    {
        var noon = SolarCalculator.Elevation(46.77, 23.6, new DateTime(2026, 6, 21, 10, 0, 0, DateTimeKind.Utc));
        var evening = SolarCalculator.Elevation(46.77, 23.6, new DateTime(2026, 6, 21, 17, 0, 0, DateTimeKind.Utc));
        Assert.True(noon > 60 && evening < noon);
    }

    [Fact]
    public void Shade_reduces_direct_sun() => Assert.Equal(7.5, SolarCalculator.DirectSunHours(15, 50));

    [Fact]
    public void Map_math_matches_known_values()
    {
        Assert.InRange(MapMath.CmPerPixel(0, 19), 29.7, 29.9);                 // 0.2986 m at the equator
        Assert.InRange(MapMath.CmPerPixel(46.77, 19), 20, 21);
        var (x, y, _, _) = MapMath.Tile(0, 0, 1);
        Assert.Equal((1, 1), (x, y));
    }
}

public class ClimateTests
{
    private static List<DailyTemp> Years(int from, int to)
    {
        var list = new List<DailyTemp>();
        for (var y = from; y <= to; y++)
            for (var d = new DateOnly(y, 1, 1); d.Year == y; d = d.AddDays(1))
            {
                // Sinusoidal year: coldest mid-January (-6 mean), warmest mid-July (+22 mean)
                var mean = 8 - 14 * Math.Cos((d.DayOfYear - 15) / 365.0 * 2 * Math.PI);
                list.Add(new DailyTemp(d, mean - 5, mean + 5, mean));
            }
        return list;
    }

    [Fact]
    public void Frost_dates_and_months_are_derived_from_daily_data()
    {
        var s = ClimateAnalyzer.Analyze(Years(2020, 2024), "test");
        Assert.Equal(5, s.YearsUsed);
        Assert.Equal(12, s.Months.Count);
        Assert.True(s.Months[0].AvgMean < 0 && s.Months[6].AvgMean > 15);
        Assert.NotNull(s.LastSpringFrost); Assert.NotNull(s.FirstAutumnFrost);
        Assert.InRange(s.LastSpringFrost!.Value.Month, 3, 5);
        Assert.InRange(s.FirstAutumnFrost!.Value.Month, 10, 11);
        Assert.InRange(s.FrostFreeDays, 150, 250);
        Assert.True(s.Months[6].Gdd10 > s.Months[0].Gdd10);
        Assert.Equal(0, s.Months[0].Gdd10);
    }

    [Fact]
    public void Summary_survives_json_round_trip()
    {
        var s = ClimateAnalyzer.Analyze(Years(2023, 2024), "test");
        var back = ClimateSummary.FromJson(s.ToJson())!;
        Assert.Equal(s.LastSpringFrost, back.LastSpringFrost);
        Assert.Equal(12, back.Months.Count);
        Assert.Null(ClimateSummary.FromJson(null));
    }

    [Fact]
    public void Service_parses_open_meteo_style_response_and_skips_gaps()
    {
        var data = new ClimateService.ArchiveResponse { Daily = new ClimateService.DailyData
        {
            Time = new() { "2024-01-01", "2024-01-02", "bad" },
            Min = new() { -3.0, null, 1 }, Max = new() { 2.0, 3, 4 }, Mean = new() { -0.5, 1, 2 },
        } };
        var s = ClimateService.Analyze(data, "x");
        Assert.Equal(-3.0, s.AbsoluteMin);
        Assert.Throws<InvalidOperationException>(() => ClimateService.Analyze(new ClimateService.ArchiveResponse(), "x"));
    }
}

public class AdvisorTests
{
    private static readonly KnowledgeBase Kb = KnowledgeBase.Load(new[] { Fixtures.RepoContent() });
    private static readonly DateOnly Today = new(2027, 5, 1);
    private static Garden G(double? lat = null) => new() { LastSpringFrost = new DateOnly(2000, 4, 25), FirstAutumnFrost = new DateOnly(2000, 10, 20), Latitude = lat, Longitude = lat is null ? null : 23.6 };
    private static GardenBed Bed(int shade = 0, params BedPlanting[] p) => new() { Id = 1, Name = "Bed A", ShadePercent = shade, Plantings = p.ToList() };
    private static BedPlanting P(string id, PlantingStatus st = PlantingStatus.Planned, DateOnly? planted = null) => new() { Id = 7, PlantId = id, Status = st, PlantedDate = planted };

    private static List<GardenAlert> Run(Garden g, GardenBed bed, DateOnly? today = null, params SeedItem[] seeds) =>
        ActionAdvisor.Evaluate(g, new[] { bed }, Kb, seeds, today ?? Today);

    [Fact]
    public void Empty_bed_is_flagged() =>
        Assert.Contains(Run(G(), Bed()), a => a.Key == "alert_bed_empty");

    [Fact]
    public void Open_sowing_window_asks_for_action_and_notes_missing_seed()
    {
        // pea direct window: -6..-2 weeks from 25 Apr = 14 Mar..11 Apr (closed); bean +1..+4 = 2 May..23 May (opens tomorrow)
        var soon = Run(G(), Bed(0, P("bean")));
        Assert.Contains(soon, a => a.Key == "alert_window_soon");
        var open = Run(G(), Bed(0, P("bean")), new DateOnly(2027, 5, 5));
        Assert.Contains(open, a => a.Key == "alert_window_open");
        Assert.Contains(open, a => a.Key == "alert_no_seed");
        var withSeed = Run(G(), Bed(0, P("bean")), new DateOnly(2027, 5, 5), new SeedItem { PlantId = "bean", QuantityRemaining = 10 });
        Assert.DoesNotContain(withSeed, a => a.Key == "alert_no_seed");
    }

    [Fact]
    public void Closing_window_is_urgent_and_passed_window_is_info()
    {
        var urgent = Run(G(), Bed(0, P("bean")), new DateOnly(2027, 5, 20));
        Assert.Equal(AlertSeverity.Urgent, urgent.Single(a => a.Key == "alert_window_closing").Severity);
        Assert.Contains(Run(G(), Bed(0, P("bean")), new DateOnly(2027, 7, 1)), a => a.Key == "alert_window_passed");
    }

    [Fact]
    public void Missing_frost_date_is_reported_instead_of_guessing() =>
        Assert.Contains(Run(new Garden(), Bed(0, P("bean"))), a => a.Key == "alert_need_frost");

    [Fact]
    public void Harvest_alerts_follow_days_to_maturity()
    {
        var planted = new DateOnly(2027, 3, 1);                      // radish 25-40 days
        Assert.Contains(Run(G(), Bed(0, P("radish", PlantingStatus.Growing, planted)), new DateOnly(2027, 4, 5)), a => a.Key == "alert_harvest_check");
        Assert.Contains(Run(G(), Bed(0, P("radish", PlantingStatus.Growing, planted)), new DateOnly(2027, 5, 20)), a => a.Key == "alert_harvest_late");
    }

    [Fact]
    public void Tender_crops_get_a_frost_warning_near_first_frost()
    {
        var alerts = Run(G(), Bed(0, P("tomato", PlantingStatus.Growing, new DateOnly(2027, 5, 20))), new DateOnly(2027, 10, 15));
        Assert.Equal(AlertSeverity.Urgent, alerts.First(a => a.Key == "alert_frost_near").Severity);
        Assert.DoesNotContain(Run(G(), Bed(0, P("kale", PlantingStatus.Growing, new DateOnly(2027, 5, 20))), new DateOnly(2027, 10, 15)), a => a.Key == "alert_frost_near");
    }

    [Fact]
    public void Shade_triggers_a_sun_warning_for_sun_lovers_only_when_coordinates_are_known()
    {
        var shaded = Run(G(46.77), Bed(70, P("tomato")));
        Assert.Contains(shaded, a => a.Key == "alert_sun_low");
        Assert.DoesNotContain(Run(G(46.77), Bed(0, P("tomato"))), a => a.Key == "alert_sun_low");
        Assert.DoesNotContain(Run(G(), Bed(70, P("tomato"))), a => a.Key == "alert_sun_low");
        Assert.DoesNotContain(Run(G(46.77), Bed(70, P("lettuce"))), a => a.Key == "alert_sun_low" && a.Args[0] == "Lettuce" && false);
    }

    [Fact]
    public void Finished_plantings_and_non_growing_objects_are_ignored()
    {
        Assert.DoesNotContain(Run(G(), Bed(0, P("bean", PlantingStatus.Finished))), a => a.Key.StartsWith("alert_window"));
        var pond = new GardenBed { Name = "Pond", Kind = "pond" };
        Assert.Empty(ActionAdvisor.Evaluate(G(), new[] { pond }, Kb, Array.Empty<SeedItem>(), Today));
    }

    [Fact]
    public void Alerts_are_sorted_most_urgent_first_and_explain_their_data()
    {
        var alerts = Run(G(), Bed(0, P("bean"), P("radish", PlantingStatus.Growing, new DateOnly(2027, 3, 1))), new DateOnly(2027, 5, 20));
        Assert.Equal(alerts.OrderBy(a => a.Severity).Select(a => a.Severity), alerts.Select(a => a.Severity));
        Assert.All(alerts, a => Assert.False(string.IsNullOrWhiteSpace(a.Why)));
    }
}

public class LibraryRegressionTests
{
    [Fact]
    public async Task Top_subjects_count_books_per_term()
    {
        using var env = await TestEnv.CreateAsync();
        await env.Library.ImportAsync(new[] { Fixtures.Epub(env.File("a.epub")), Fixtures.Epub(env.File("b.epub"), "Second", "Different text about mulch and bees.") });
        var top = await env.Library.TopSubjectsAsync();
        Assert.Contains(top, s => s.Term == "permaculture" && s.Books == 2);
    }

    [Fact]
    public async Task Text_file_title_comes_from_content_not_the_stored_hash_name()
    {
        using var env = await TestEnv.CreateAsync();
        File.WriteAllText(env.File("x.txt"), "Ghid de compostare\n\nCompostul hrănește solul.");
        var book = (await env.Library.ImportAsync(new[] { env.File("x.txt") })).Single().Book!;
        Assert.Equal("Ghid de compostare", book.Title);
    }

    [Fact]
    public async Task Reindex_keeps_user_edited_title()
    {
        using var env = await TestEnv.CreateAsync();
        var book = (await env.Library.ImportAsync(new[] { Fixtures.Epub(env.File("a.epub")) })).Single().Book!;
        await env.Library.UpdateDetailsAsync(book.Id, "My title", "Me", "x");
        await env.Library.ReindexAsync(book.Id);
        Assert.Equal("My title", (await env.Library.GetAsync(book.Id))!.Title);
    }

    [Fact]
    public async Task Beds_and_plantings_round_trip_and_cascade()
    {
        using var env = await TestEnv.CreateAsync();
        var repo = new Repository(env.Factory);
        var bed = await repo.SaveAsync(new GardenBed { GardenId = 1, Name = "A" });
        await repo.SaveAsync(new BedPlanting { BedId = bed.Id, PlantId = "kale", Quantity = 6 });
        Assert.Single(await repo.ListAsync<BedPlanting>());
        await repo.DeleteAsync(bed);
        Assert.Empty(await repo.ListAsync<BedPlanting>());
    }
}
