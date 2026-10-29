using System.Text.RegularExpressions;

namespace GardenHelper.Tests;

/// <summary>Keeps UI text complete: every key used in XAML or code exists in English, and Romanian has the same keys.</summary>
public class LocalizationTests
{
    private static string AppDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "GardenHelper.App"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "src", "GardenHelper.App");
    }

    private static HashSet<string> Keys(string language)
    {
        var text = File.ReadAllText(Path.Combine(AppDir(), "Infrastructure", "Strings.cs"));
        var start = text.IndexOf(language == "en" ? "En() => new()" : "Ro() => new()", StringComparison.Ordinal);
        var end = language == "en" ? text.IndexOf("Ro() => new()", StringComparison.Ordinal) : text.Length;
        return Regex.Matches(text[start..end], @"\[""([A-Za-z0-9_]+)""\]\s*=").Select(m => m.Groups[1].Value).ToHashSet();
    }

    [Fact]
    public void Romanian_and_english_have_the_same_keys()
    {
        var en = Keys("en"); var ro = Keys("ro");
        Assert.Empty(en.Except(ro));
        Assert.Empty(ro.Except(en));
    }

    [Fact]
    public void Every_key_used_in_xaml_and_code_exists()
    {
        var known = Keys("en");
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(AppDir(), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".xaml") || f.EndsWith(".cs")) && !f.Contains(@"\obj\") && !f.Contains(@"\bin\")))
        {
            var t = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(t, @"loc:L ([A-Za-z0-9_]+)")) used.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(t, @"Loc\.[TF]\(""([A-Za-z0-9_]+)""")) used.Add(m.Groups[1].Value);
        }
        // Keys built from a prefix at run time (nav_, act_, task_, kind_, status_, sev_, search_kind_) are checked by their prefix.
        var dynamicPrefixes = new[] { "nav_", "act_", "task_", "kind_", "status_", "sev_", "search_kind_", "rotv_", "feat_", "log_", "cam_kind_", "animal_", "alog_", "bcat_", "up_bucket_", "inv_f_", "inv_loc_", "inv_st_", "inv_log_", "tree_type_", "tact_", "gh_m_", "gh_act_", "gh_sys_", "gh_item_", "int_k_", "int_g_", "int_floor_", "gh_alert_", "purpose_", "origin_", "edib_", "pal_", "cam_brand_", "cam_ev_" };
        var missing = used.Where(k => !known.Contains(k) && !dynamicPrefixes.Contains(k)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Dynamic_keys_exist_for_every_value()
    {
        var known = Keys("en");
        var expected = new[]
        {
            "nav_home","nav_dashboard","nav_tasks","nav_monitor","nav_plan","nav_designer","nav_site","nav_grow","nav_plants","nav_seeds","nav_journal","nav_learn","nav_library","nav_system","nav_garden","nav_settings","nav_about",
            "act_SowIndoors","act_SowDirect","act_Transplant","act_Plant",
            "task_SowIndoors","task_SowDirect","task_Transplant","task_Water","task_Feed","task_Prune","task_Harvest","task_Inspect","task_Other",
            "kind_bed","kind_raised","kind_greenhouse","kind_tree","kind_pond","kind_compost","kind_path","kind_structure",
            "status_Planned","status_Sown","status_Growing","status_Harvesting","status_Finished","sev_Urgent","sev_Soon","sev_Info",
            "search_kind_Plant","search_kind_Book","search_kind_Journal","search_kind_Task","search_kind_Seed","search_kind_Feature","nav_property",
        };
        expected = expected
            .Concat(GardenHelper.Core.Cameras.CameraUrl.Kinds.Select(k => "cam_kind_" + k))
            .Concat(GardenHelper.Core.Planning.BudgetCatalog.Categories.Append("animals").Select(k => "bcat_" + k))
            .Concat(GardenHelper.Core.Planning.ComingUpPlanner.Buckets.Select(k => "up_bucket_" + k))
            .Concat(new[] { "all", "Outdoor", "Greenhouse", "Indoor", "trees" }.Select(k => "inv_f_" + k))
            .Concat(new[] { "Outdoor", "Greenhouse", "Indoor" }.Select(k => "inv_loc_" + k))
            .Concat(GardenHelper.Core.Domain.InventoryRules.Statuses.Select(k => "inv_st_" + k))
            .Concat(GardenHelper.Core.Domain.InventoryRules.LogKinds.Select(k => "inv_log_" + k))
            .Concat(GardenHelper.Core.Planning.TreeRules.Types.Select(k => "tree_type_" + k))
            .Concat(GardenHelper.Core.Planning.TreeRules.AllActionKeys.Select(k => "tact_" + k))
            .Concat(GardenHelper.Core.Cameras.CameraPresets.All.Select(p => "cam_brand_" + p.Key.Replace('-', '_')))
            .Concat(new[] { "motion", "sensor", "snapshot", "manual" }.Select(k => "cam_ev_" + k))
            .Concat(GardenHelper.Core.Design.PlanElements.Objects.Select(e => "kind_" + e.Key))
            .Concat(GardenHelper.Core.Design.PlanElements.Groups.Select(g => "pal_" + g))
            .Concat(GardenHelper.Core.Knowledge.PlantPurpose.Keys.Select(k => "purpose_" + k))
            .Concat(GardenHelper.Core.Knowledge.PlantOrigin.All.Select(k => "origin_" + k.Replace('-', '_')))
            .Concat(GardenHelper.Core.Knowledge.Edibility.All.Select(k => "edib_" + k.Replace('-', '_')))
            .Concat(GardenHelper.Core.Greenhouse.GreenhouseCatalog.Metrics.Select(m => "gh_m_" + m.Key))
            .Concat(GardenHelper.Core.Greenhouse.GreenhouseCatalog.ActuatorKeys.Select(k => "gh_act_" + k))
            .Concat(GardenHelper.Core.Greenhouse.GreenhouseCatalog.Systems.Select(k => "gh_sys_" + k))
            .Concat(GardenHelper.Core.Greenhouse.GreenhouseCatalog.ItemKinds.Select(k => "gh_item_" + k))
            .Concat(GardenHelper.Core.Design.InteriorCatalog.All.Select(k => "int_k_" + k.Key))
            .Concat(GardenHelper.Core.Design.InteriorCatalog.Floors.Select(f => "int_floor_" + GardenHelper.Core.Design.InteriorCatalog.FloorKey(f)))
            .Concat(GardenHelper.Core.Design.InteriorCatalog.Groups.Select(g => "int_g_" + g))
            .Concat(new[] { "no_data", "stale", "frost", "cold", "hot", "humid", "dry", "water_hot", "water_hot_fish", "ph", "level" }.Select(k => "gh_alert_" + k))
            .Concat(GardenHelper.Core.Animals.AnimalCatalog.Species.Select(k => "animal_" + k))
            .Concat(GardenHelper.Core.Animals.AnimalCatalog.LogKinds.Select(k => "alog_" + k))
            .Concat(GardenHelper.Core.Domain.FeatureCatalog.Structures.Concat(GardenHelper.Core.Domain.FeatureCatalog.Spaces).Select(k => "feat_" + k))
            .Concat(new[] { "feat_outline" })
            .Concat(GardenHelper.Core.Domain.FeatureCatalog.LogKinds.Select(k => "log_" + k)).ToArray();
        Assert.Empty(expected.Where(k => !known.Contains(k)));
    }

    [Fact]
    public void Advisor_alert_keys_are_all_translated()
    {
        var known = Keys("en");
        var core = Path.Combine(AppDir(), "..", "GardenHelper.Core", "Services");
        var src = File.ReadAllText(Path.Combine(core, "ActionAdvisor.cs")) + File.ReadAllText(Path.Combine(core, "RotationAndCalendar.cs"));
        var keys = Regex.Matches(src, @"""((?:alert|rot)_[a-z_]+)""").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(keys);
        Assert.Empty(keys.Where(k => !known.Contains(k)));
    }

    [Fact]
    public void Content_enum_values_have_translations()
    {
        var known = Keys("en");
        var kb = GardenHelper.Core.Knowledge.KnowledgeBase.Load(new[] { Fixtures.RepoContent() });
        static string K(string g, string v) => g + "_" + v.Replace('-', '_');
        var missing = new List<string>();
        foreach (var p in kb.Plants)
        {
            if (!known.Contains(K("cat", p.Category))) missing.Add(K("cat", p.Category));
            if (p.Sun is not null && !known.Contains(K("sun", p.Sun))) missing.Add(K("sun", p.Sun));
            if (p.Water is not null && !known.Contains(K("water", p.Water))) missing.Add(K("water", p.Water));
            if (p.FrostTolerance is not null && !known.Contains(K("frost", p.FrostTolerance))) missing.Add(K("frost", p.FrostTolerance));
            missing.AddRange(p.Roles.Where(r => !known.Contains(K("role", r))).Select(r => K("role", r)));
        }
        foreach (var r in kb.Relationships)
        {
            if (!known.Contains(K("relcat", r.Category))) missing.Add(K("relcat", r.Category));
            if (!known.Contains(K("evidence", r.Evidence))) missing.Add(K("evidence", r.Evidence));
            if (!known.Contains(K("effect", r.Effect))) missing.Add(K("effect", r.Effect));
        }
        Assert.Empty(missing.Distinct());
    }
}
