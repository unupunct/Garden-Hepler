using GardenHelper.Core.Data;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GardenHelper.Tests;

public class KnowledgeTests
{
    private static KnowledgeBase Kb() => KnowledgeBase.Load(new[] { Fixtures.RepoContent() });

    [Fact]
    public void Shipped_content_passes_validation()
    {
        var kb = Kb();
        Assert.True(kb.Plants.Count >= 100);
        Assert.Equal(kb.Plants.Count, kb.Plants.Select(p => p.Id).Distinct().Count());
        // Mushrooms must carry a "cultivation only" caution in both languages.
        Assert.All(kb.Plants.Where(p => p.Category == "mushroom"), p => { Assert.NotNull(p.CautionText("en")); Assert.NotNull(p.CautionText("ro")); });
        Assert.Empty(kb.Validate());
    }

    [Fact]
    public void Every_plant_has_a_romanian_name_and_summary()
    {
        foreach (var p in Kb().Plants)
        {
            Assert.True(p.Names.ContainsKey("ro"), p.Id);
            Assert.True(p.Summary.ContainsKey("ro"), p.Id);
        }
    }

    [Theory]
    [InlineData("rosie", "tomato")]        // diacritics folded: Roșie
    [InlineData("Solanum", "tomato")]
    [InlineData("morcov", "carrot")]
    [InlineData("topinambur", "jerusalem-artichoke")]
    [InlineData("zucchini", "courgette")]
    [InlineData("nitrogen-fixer", "pea")]
    [InlineData("prun", "plum")]                    // Romanian names, accents optional
    [InlineData("catina", "sea-buckthorn")]         // cătină
    [InlineData("vanata", "eggplant")]              // vânătă
    [InlineData("lubenita", "watermelon")]          // lubeniță
    [InlineData("macris", "sorrel")]                // măcriș
    [InlineData("leustean", "lovage")]              // leuștean
    [InlineData("viță de vie", "grape")]
    [InlineData("stropharia", "wine-cap")]
    [InlineData("gutui", "quince")]
    public void Search_finds_by_any_name_language_or_role(string query, string expectedId) =>
        Assert.Contains(Kb().Search(query), m => m.Plant.Id == expectedId);

    [Fact]
    public void Search_ranks_exact_name_first_and_filters_by_category()
    {
        var kb = Kb();
        Assert.Equal("tomato", kb.Search("tomato").First().Plant.Id);
        Assert.All(kb.Search(null, "cover-crop"), m => Assert.Equal("cover-crop", m.Plant.Category));
        Assert.Empty(kb.Search("qqqq"));
    }

    [Fact]
    public void Relationships_carry_evidence_levels_and_explanations()
    {
        var kb = Kb();
        var rels = kb.RelationshipsFor("tomato").ToList();
        Assert.Contains(rels, r => r.Other("tomato") == "potato" && r.Effect == "negative");
        Assert.All(kb.Relationships, r => Assert.False(string.IsNullOrWhiteSpace(r.ExplanationText("en"))));
    }

    [Fact]
    public void Validation_reports_problems_in_user_content()
    {
        using var env = TempDir();
        Directory.CreateDirectory(Path.Combine(env, "plants"));
        File.WriteAllText(Path.Combine(env, "plants", "bad.json"),
            "[{\"id\":\"Bad Id\",\"scientific\":\"\",\"names\":{},\"summary\":{},\"sowing\":{\"anchor\":\"x\",\"direct\":{\"min\":5,\"max\":1}}}]");
        File.WriteAllText(Path.Combine(env, "relationships.json"), "[{\"a\":\"nope\",\"b\":\"nope2\",\"evidence\":\"vibes\"}]");
        File.WriteAllText(Path.Combine(env, "plants", "broken.json"), "{ not json");

        var problems = KnowledgeBase.Load(new[] { (string)env }).Validate();

        Assert.Contains(problems, p => p.Contains("id must be lower-case"));
        Assert.Contains(problems, p => p.Contains("scientific name is required"));
        Assert.Contains(problems, p => p.Contains("min greater than max"));
        Assert.Contains(problems, p => p.Contains("unknown plant 'nope'"));
        Assert.Contains(problems, p => p.Contains("invalid evidence level"));
        Assert.Contains(problems, p => p.Contains("broken.json"));
    }

    [Fact]
    public void User_content_overrides_builtin_ids()
    {
        using var user = TempDir();
        Directory.CreateDirectory(Path.Combine(user, "plants"));
        File.WriteAllText(Path.Combine(user, "plants", "mine.json"),
            "[{\"id\":\"tomato\",\"scientific\":\"Solanum lycopersicum\",\"names\":{\"en\":[\"My tomato\"]},\"summary\":{\"en\":\"x\"}}]");
        var kb = KnowledgeBase.Load(new[] { Fixtures.RepoContent(), (string)user });
        Assert.Equal("My tomato", kb.Get("tomato")!.DisplayName("en"));
        Assert.True(kb.Get("tomato")!.IsUserContent);
    }

    private static TempDirHandle TempDir() => new();
    private sealed class TempDirHandle : IDisposable
    {
        private readonly string _p = Path.Combine(Path.GetTempPath(), "gh-kb-" + Guid.NewGuid().ToString("N")[..8]);
        public TempDirHandle() => Directory.CreateDirectory(_p);
        public void Dispose() { try { Directory.Delete(_p, true); } catch (IOException) { } }
        public static implicit operator string(TempDirHandle h) => h._p;
    }
}

public class PlantingCalculatorTests
{
    private static PlantRecord Plant(string id) => KnowledgeBase.Load(new[] { Fixtures.RepoContent() }).Get(id)!;
    private static Garden Garden(int m, int d, bool autumn = false) =>
        autumn ? new Garden { FirstAutumnFrost = new DateOnly(2000, m, d) } : new Garden { LastSpringFrost = new DateOnly(2000, m, d) };

    [Fact]
    public void Tomato_windows_follow_last_frost()
    {
        var plan = PlantingCalculator.Calculate(Plant("tomato"), Garden(5, 10), 2027);
        Assert.True(plan.Personalized);
        var indoor = plan.Windows.Single(w => w.Activity == PlantingActivity.SowIndoors);
        Assert.Equal(new DateOnly(2027, 3, 15), indoor.Start);       // 8 weeks before 10 May
        Assert.Equal(new DateOnly(2027, 3, 29), indoor.End);         // 6 weeks before
        var transplant = plan.Windows.Single(w => w.Activity == PlantingActivity.Transplant);
        Assert.Equal(new DateOnly(2027, 5, 17), transplant.Start);
        Assert.Equal(new DateOnly(2027, 5, 24), transplant.End);
    }

    [Fact]
    public void Shift_moves_every_window()
    {
        var a = PlantingCalculator.Calculate(Plant("tomato"), Garden(5, 10), 2027);
        var b = PlantingCalculator.Calculate(Plant("tomato"), Garden(5, 10), 2027, shiftDays: 7);
        Assert.Equal(a.Windows[0].Start.AddDays(7), b.Windows[0].Start);
    }

    [Fact]
    public void Garlic_uses_the_autumn_frost_anchor()
    {
        var plan = PlantingCalculator.Calculate(Plant("garlic"), Garden(10, 20, autumn: true), 2026);
        var w = Assert.Single(plan.Windows);
        Assert.Equal(PlantingActivity.Plant, w.Activity);
        Assert.Equal(new DateOnly(2026, 10, 6), w.Start);
    }

    [Fact]
    public void Missing_frost_date_is_not_presented_as_personalised()
    {
        var plan = PlantingCalculator.Calculate(Plant("tomato"), new Garden(), 2027);
        Assert.False(plan.Personalized);
        Assert.Empty(plan.Windows);
        Assert.Equal("noFrostDate", plan.Reason);
        Assert.False(PlantingCalculator.Calculate(Plant("tomato"), null, 2027).Personalized);
    }

    [Fact]
    public void Plants_without_sowing_data_have_no_windows()
    {
        var plan = PlantingCalculator.Calculate(Plant("oyster-mushroom"), Garden(5, 1), 2027);
        Assert.Equal("noData", plan.Reason);
    }

    [Fact]
    public void Feb_29_frost_date_is_handled_in_non_leap_years() =>
        Assert.Equal(new DateOnly(2027, 2, 28), PlantingCalculator.SafeDate(2027, 2, 29));

    [Theory]
    [InlineData(1, false, "winter")]
    [InlineData(4, false, "spring")]
    [InlineData(7, false, "summer")]
    [InlineData(10, false, "autumn")]
    [InlineData(1, true, "summer")]
    public void Season_by_hemisphere(int month, bool south, string expected) =>
        Assert.Equal(expected, PlantingCalculator.Season(new DateOnly(2026, month, 15), south));
}

public class DatabaseTests
{
    [Fact]
    public async Task Migration_creates_schema_and_full_text_index_stays_in_sync()
    {
        using var env = await TestEnv.CreateAsync();
        await using var db = env.Factory.CreateDbContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        db.Books.Add(new Book { Title = "T", Format = "TXT", StoredFile = "x", Sha256 = "h", Sections = { new BookSection { Ordinal = 1, Title = "S", Text = "alpha beta" } } });
        await db.SaveChangesAsync();
        Assert.Single(await env.Library.SearchAsync("alpha"));

        await db.BookSections.ExecuteUpdateAsync(s => s.SetProperty(x => x.Text, "gamma"));
        Assert.Empty(await env.Library.SearchAsync("alpha"));
        Assert.Single(await env.Library.SearchAsync("gamma"));
    }

    [Fact]
    public async Task Crud_round_trip_for_user_records()
    {
        using var env = await TestEnv.CreateAsync();
        var repo = new Repository(env.Factory);
        var task = await repo.SaveAsync(new GardenTask { Title = "Sow peas", DueDate = new DateOnly(2027, 3, 1) });
        Assert.NotEqual(0, task.Id);
        task.Done = true;
        await repo.SaveAsync(task);
        Assert.True((await repo.ListAsync<GardenTask>()).Single().Done);
        await repo.DeleteAsync(task);
        Assert.Empty(await repo.ListAsync<GardenTask>());
    }

    [Fact]
    public async Task Settings_persist()
    {
        using var env = await TestEnv.CreateAsync();
        var s = new SettingsService(env.Factory);
        Assert.Null(await s.GetAsync("lang"));
        await s.SetAsync("lang", "ro");
        await s.SetAsync("lang", "en");
        Assert.Equal("en", await s.GetAsync("lang"));
    }

    [Fact]
    public async Task Backup_is_verified_and_restore_replaces_the_database_at_next_start()
    {
        using var env = await TestEnv.CreateAsync();
        var repo = new Repository(env.Factory);
        await repo.SaveAsync(new JournalEntry { Title = "before backup" });
        var backups = new BackupService(env.Paths, NullLogger<BackupService>.Instance);
        var backup = await backups.CreateAsync();
        Assert.Null(await BackupService.VerifyAsync(backup.Path));

        await repo.SaveAsync(new JournalEntry { Title = "after backup" });
        Assert.Equal(2, (await repo.ListAsync<JournalEntry>()).Count);

        await backups.ScheduleRestoreAsync(backup.Path);
        SqliteConnection.ClearAllPools();
        DbFactory.ApplyPendingRestore(env.Paths);

        var restored = await new Repository(env.Factory).ListAsync<JournalEntry>();
        Assert.Equal("before backup", Assert.Single(restored).Title);
        Assert.Contains(backups.List(), b => b.Path.Contains("before-restore"));
    }

    [Fact]
    public async Task Corrupt_or_foreign_files_cannot_be_restored()
    {
        using var env = await TestEnv.CreateAsync();
        var backups = new BackupService(env.Paths, NullLogger<BackupService>.Instance);
        var junk = env.File("junk.db");
        File.WriteAllText(junk, "not a database");
        await Assert.ThrowsAsync<InvalidOperationException>(() => backups.ScheduleRestoreAsync(junk));

        var foreign = env.File("foreign.db");
        await using (var c = new SqliteConnection($"Data Source={foreign};Pooling=False"))
        {
            await c.OpenAsync();
            var cmd = c.CreateCommand(); cmd.CommandText = "CREATE TABLE x(a)"; await cmd.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => backups.ScheduleRestoreAsync(foreign));
        Assert.False(File.Exists(env.Paths.PendingRestoreFile));
    }

    [Fact]
    public async Task Auto_backup_runs_only_when_due()
    {
        using var env = await TestEnv.CreateAsync();
        var backups = new BackupService(env.Paths, NullLogger<BackupService>.Instance);
        Assert.NotNull(await backups.AutoBackupIfDueAsync(TimeSpan.FromDays(7)));
        Assert.Null(await backups.AutoBackupIfDueAsync(TimeSpan.FromDays(7)));
    }

    [Fact]
    public async Task Global_search_spans_plants_records_and_books()
    {
        using var env = await TestEnv.CreateAsync();
        var repo = new Repository(env.Factory);
        await repo.SaveAsync(new JournalEntry { Title = "Tomato trial", Text = "planted rosii outside" });
        await repo.SaveAsync(new SeedItem { Name = "Tomato", Variety = "Cherry", Supplier = "Local" });
        await env.Library.ImportAsync(new[] { Fixtures.Epub(env.File("a.epub")) });
        var search = new GlobalSearchService(KnowledgeBase.Load(new[] { Fixtures.RepoContent() }), env.Library, repo);

        var results = await search.SearchAsync("tomato", "en");

        Assert.Contains(results, r => r.Kind == SearchKind.Plant && r.TargetId == "tomato");
        Assert.Contains(results, r => r.Kind == SearchKind.Journal);
        Assert.Contains(results, r => r.Kind == SearchKind.Seed);
        Assert.Contains(results, r => r.Kind == SearchKind.Book && r.Ordinal == 1);
    }
}
