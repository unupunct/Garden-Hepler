using System.Text.Json;
using GardenHelper.Core.Library;
using Microsoft.Extensions.Logging;

namespace GardenHelper.Core.Knowledge;

public sealed record PlantMatch(PlantRecord Plant, int Score);

/// <summary>
/// Plant knowledge loaded from JSON content files (see docs/CONTENT.md). Built-in content ships with the
/// application; extra files in the user's content folder are merged in and override built-in ids.
/// </summary>
public sealed class KnowledgeBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private readonly Dictionary<string, PlantRecord> _plants = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PlantRelationship> _relationships = new();
    private readonly Dictionary<string, string> _haystack = new();

    public IReadOnlyCollection<PlantRecord> Plants => _plants.Values;
    public IReadOnlyList<PlantRelationship> Relationships => _relationships;
    private readonly Dictionary<string, RotationRule> _rotation = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<RotationRule> RotationRules => _rotation.Values;
    public RotationRule? RuleFor(string? family) => family is not null && _rotation.TryGetValue(family, out var r) ? r : null;
    public List<string> LoadErrors { get; } = new();

    public static KnowledgeBase Load(IEnumerable<string> contentDirs, ILogger? log = null)
    {
        var kb = new KnowledgeBase();
        var first = true;
        foreach (var dir in contentDirs)
        {
            if (!Directory.Exists(dir)) { first = false; continue; }
            var user = !first;
            first = false;
            var plantsDir = Path.Combine(dir, "plants");
            if (Directory.Exists(plantsDir))
                foreach (var file in Directory.EnumerateFiles(plantsDir, "*.json").OrderBy(f => f))
                    kb.LoadFile<List<PlantRecord>>(file, list => { foreach (var p in list) { p.IsUserContent = user; kb._plants[p.Id] = p; } }, log);
            var rot = Path.Combine(dir, "rotation-rules.json");
            if (File.Exists(rot))
                kb.LoadFile<List<RotationRule>>(rot, list => { foreach (var r in list) kb._rotation[r.Family] = r; }, log);
            var rel = Path.Combine(dir, "relationships.json");
            if (File.Exists(rel))
                kb.LoadFile<List<PlantRelationship>>(rel, list => kb._relationships.AddRange(list), log);
        }
        kb.BuildIndex();
        return kb;
    }

    private void LoadFile<T>(string file, Action<T> apply, ILogger? log)
    {
        try
        {
            var data = JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json);
            if (data is not null) apply(data);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A broken content file must not stop the application; report it and keep the rest.
            LoadErrors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            log?.LogError(ex, "Could not load content file {File}", file);
        }
    }

    private void BuildIndex()
    {
        foreach (var p in _plants.Values)
        {
            var parts = p.Names.Values.SelectMany(x => x).Concat(p.Synonyms).Append(p.Scientific).Append(p.Family ?? "").Append(p.Category).Concat(p.Roles).Append(p.Origin ?? "")
                .Concat(p.Medicinal?.Uses.Values ?? Enumerable.Empty<string>()).Concat(p.Medicinal?.Parts.Values ?? Enumerable.Empty<string>()).Concat(p.Benefits?.Values ?? Enumerable.Empty<string>());
            _haystack[p.Id] = SubjectExtractor.Fold(string.Join(" | ", parts));
        }
    }

    public PlantRecord? Get(string? id) => id is not null && _plants.TryGetValue(id, out var p) ? p : null;

    /// <summary>Plants that can be grown in a garden. Wild mushrooms are for recognition only and are left out of planting lists.</summary>
    public IEnumerable<PlantRecord> Plantable => _plants.Values.Where(p => p.Category != "wild-mushroom");

    public IReadOnlyList<string> Categories => _plants.Values.Select(p => p.Category).Distinct().OrderBy(x => x).ToList();

    /// <summary>Searches common names in every language, scientific names, synonyms, family and roles; ignores case and diacritics.</summary>
    public List<PlantMatch> Search(string? query, string? category = null, string? purpose = null)
    {
        IEnumerable<PlantRecord> pool = _plants.Values;
        if (!string.IsNullOrWhiteSpace(category)) pool = pool.Where(p => p.Category == category);
        if (!string.IsNullOrWhiteSpace(purpose)) pool = pool.Where(p => PlantPurpose.Matches(p, purpose));
        if (string.IsNullOrWhiteSpace(query))
            return pool.Select(p => new PlantMatch(p, 0)).OrderBy(m => m.Plant.DisplayName("en"), StringComparer.OrdinalIgnoreCase).ToList();

        var tokens = SubjectExtractor.Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var results = new List<PlantMatch>();
        foreach (var p in pool)
        {
            var hay = _haystack[p.Id];
            var score = 0;
            var all = true;
            foreach (var t in tokens)
            {
                if (!hay.Contains(t)) { all = false; break; }
                var names = p.Names.Values.SelectMany(x => x).Append(p.Scientific).Concat(p.Synonyms).Select(SubjectExtractor.Fold).ToList();
                score += names.Any(n => n == t) ? 100 : names.Any(n => n.StartsWith(t)) ? 60 : names.Any(n => n.Split(' ').Any(w => w.StartsWith(t))) ? 40 : 10;
            }
            if (all) results.Add(new PlantMatch(p, score));
        }
        return results.OrderByDescending(r => r.Score).ThenBy(r => r.Plant.DisplayName("en"), StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IEnumerable<PlantRelationship> RelationshipsFor(string plantId) => _relationships.Where(r => r.Involves(plantId));

    /// <summary>Checks content for problems a contributor should fix. Returns human-readable messages.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>(LoadErrors);
        foreach (var p in _plants.Values)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || p.Id != p.Id.ToLowerInvariant() || p.Id.Contains(' ')) problems.Add($"Plant '{p.Id}': id must be lower-case without spaces.");
            if (string.IsNullOrWhiteSpace(p.Scientific)) problems.Add($"Plant '{p.Id}': scientific name is required.");
            if (!p.Names.TryGetValue("en", out var en) || en.Length == 0) problems.Add($"Plant '{p.Id}': an English common name is required.");
            if (!p.Summary.ContainsKey("en")) problems.Add($"Plant '{p.Id}': English summary is required.");
            if (p.Sources.Length == 0) problems.Add($"Plant '{p.Id}': at least one source entry is required.");
            foreach (var (label, r) in new[] { ("indoors", p.Sowing?.Indoors), ("direct", p.Sowing?.Direct), ("transplant", p.Sowing?.Transplant), ("plant", p.Sowing?.Plant), ("daysToMaturity", p.DaysToMaturity), ("spacingCm", p.SpacingCm) })
                if (r is not null && r.Min > r.Max) problems.Add($"Plant '{p.Id}': {label} range has min greater than max.");
            if (p.Origin is not null && !PlantOrigin.All.Contains(p.Origin)) problems.Add($"Plant '{p.Id}': origin must be one of {string.Join(", ", PlantOrigin.All)}.");
            if (p.Edibility is not null && !Edibility.All.Contains(p.Edibility)) problems.Add($"Plant '{p.Id}': edibility must be one of {string.Join(", ", Edibility.All)}.");
            if (p.SeasonMonths is { } sm && (sm.Min < 1 || sm.Max > 12)) problems.Add($"Plant '{p.Id}': seasonMonths must be within 1-12 (a range that wraps the year end is not supported: split it).");
            if (p.Category == "wild-mushroom")
            {
                if (p.Edibility is null) problems.Add($"Plant '{p.Id}': a wild mushroom needs an edibility rating.");
                if (string.IsNullOrWhiteSpace(p.CautionText("en")) || string.IsNullOrWhiteSpace(p.CautionText("ro"))) problems.Add($"Plant '{p.Id}': a wild mushroom needs a caution in English and Romanian.");
            }
            if (p.Medicinal is { } med && (!med.Safety.ContainsKey("en") || !med.Safety.ContainsKey("ro"))) problems.Add($"Plant '{p.Id}': medicinal use needs safety text in English and Romanian.");
            if (p.Sowing is { Anchor: not ("lastFrost" or "firstFrost") }) problems.Add($"Plant '{p.Id}': sowing anchor must be lastFrost or firstFrost.");
        }
        foreach (var r in _relationships)
        {
            if (!_plants.ContainsKey(r.A)) problems.Add($"Relationship {r.A}/{r.B}: unknown plant '{r.A}'.");
            if (!_plants.ContainsKey(r.B)) problems.Add($"Relationship {r.A}/{r.B}: unknown plant '{r.B}'.");
            if (r.Evidence is not ("research" or "recommendation" or "traditional" or "anecdotal")) problems.Add($"Relationship {r.A}/{r.B}: invalid evidence level '{r.Evidence}'.");
            if (r.Effect is not ("positive" or "negative" or "mixed" or "uncertain")) problems.Add($"Relationship {r.A}/{r.B}: invalid effect '{r.Effect}'.");
            if (!r.Explanation.ContainsKey("en")) problems.Add($"Relationship {r.A}/{r.B}: English explanation is required.");
        }
        return problems;
    }
}
