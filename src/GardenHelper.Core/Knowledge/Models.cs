using System.Text.Json.Serialization;

namespace GardenHelper.Core.Knowledge;

public sealed record IntRange(int Min, int Max);

public sealed class SourceRef
{
    public string Title { get; set; } = "";
    public string? Organization { get; set; }
    public string? Url { get; set; }
    public string? Accessed { get; set; }
}

/// <summary>Sowing and planting windows in weeks relative to an anchor frost date (negative = before).</summary>
public sealed class Sowing
{
    /// <summary>lastFrost (default) or firstFrost.</summary>
    public string Anchor { get; set; } = "lastFrost";
    public IntRange? Indoors { get; set; }
    public IntRange? Direct { get; set; }
    public IntRange? Transplant { get; set; }
    /// <summary>Tubers, crowns, bulbs, canes: planted rather than sown.</summary>
    public IntRange? Plant { get; set; }
    public Dictionary<string, string>? Note { get; set; }
}

/// <summary>Traditional medicinal use of a plant. Always shown with its safety text; never presented as medical advice.</summary>
public sealed class MedicinalInfo
{
    /// <summary>Which parts are used (flowers, leaves, root...).</summary>
    public Dictionary<string, string> Parts { get; set; } = new();
    /// <summary>Traditional uses and usual preparations.</summary>
    public Dictionary<string, string> Uses { get; set; } = new();
    /// <summary>Who should not use it, side effects, interactions, harvesting limits.</summary>
    public Dictionary<string, string> Safety { get; set; } = new();
}

public static class PlantOrigin
{
    public static readonly string[] All = { "native-ro", "naturalized", "introduced", "invasive" };
}

public static class Edibility
{
    public static readonly string[] All = { "edible", "edible-cooked", "inedible", "toxic", "deadly" };
}

/// <summary>The garden jobs a plant can do. Used by the encyclopedia filter chips.</summary>
public static class PlantPurpose
{
    public static readonly string[] Keys = { "native", "pollinator", "pest", "soil", "medicinal", "edible" };
    private static readonly string[] PestRoles = { "beneficial-insects", "pest-confuser", "trap-crop", "pest-repellent" };
    private static readonly string[] SoilRoles = { "nitrogen-fixer", "green-manure", "compost-activator", "biomass", "decomposer", "ground-cover", "living-mulch", "dynamic-accumulator", "weed-suppression" };

    public static bool Matches(PlantRecord p, string purpose) => purpose switch
    {
        "native" => p.Origin == "native-ro",
        "pollinator" => p.Roles.Contains("pollinator"),
        "pest" => p.Roles.Any(r => PestRoles.Contains(r)),
        "soil" => p.NitrogenFixing || p.Roles.Any(r => SoilRoles.Contains(r)),
        "medicinal" => p.Medicinal is not null || p.Roles.Contains("medicinal"),
        "edible" => p.Roles.Contains("food") || p.Roles.Contains("edible-flower") || p.Edibility is "edible" or "edible-cooked",
        _ => true,
    };
}

public sealed class PlantRecord
{
    public string Id { get; set; } = "";
    public Dictionary<string, string[]> Names { get; set; } = new();
    public string Scientific { get; set; } = "";
    public string[] Synonyms { get; set; } = Array.Empty<string>();
    public string? Family { get; set; }
    public string Category { get; set; } = "";
    public string? Lifecycle { get; set; }
    public string? Sun { get; set; }
    public string? Water { get; set; }
    public string? FrostTolerance { get; set; }
    public Dictionary<string, string> Summary { get; set; } = new();
    public Dictionary<string, string[]> Tips { get; set; } = new();
    public Dictionary<string, string>? Cautions { get; set; }
    public Sowing? Sowing { get; set; }
    public IntRange? DaysToMaturity { get; set; }
    public IntRange? SpacingCm { get; set; }
    public IntRange? RowSpacingCm { get; set; }
    public IntRange? HeightCm { get; set; }
    public string? RotationFamily { get; set; }
    public string[] Roles { get; set; } = Array.Empty<string>();
    public bool NitrogenFixing { get; set; }
    /// <summary>native-ro (grows wild in Romania), naturalized, introduced or invasive.</summary>
    public string? Origin { get; set; }
    /// <summary>Months (1-12) when it flowers, or for mushrooms when it fruits.</summary>
    public IntRange? SeasonMonths { get; set; }
    /// <summary>Mushrooms only: edible, edible-cooked, inedible, toxic or deadly.</summary>
    public string? Edibility { get; set; }
    /// <summary>Protected or regulated in Romania: do not pick it in the wild.</summary>
    public bool Protected { get; set; }
    /// <summary>What it does for the garden (pollinators, pests, soil).</summary>
    public Dictionary<string, string>? Benefits { get; set; }
    public MedicinalInfo? Medicinal { get; set; }
    public string Confidence { get; set; } = "moderate";
    public string? Reviewed { get; set; }
    public SourceRef[] Sources { get; set; } = Array.Empty<SourceRef>();
    [JsonIgnore] public bool IsUserContent { get; set; }

    public string DisplayName(string lang) =>
        (Names.GetValueOrDefault(lang)?.FirstOrDefault()) ?? Names.GetValueOrDefault("en")?.FirstOrDefault() ?? Scientific;

    public string SummaryText(string lang) => Summary.GetValueOrDefault(lang) ?? Summary.GetValueOrDefault("en") ?? "";
    public string[] TipList(string lang) => Tips.GetValueOrDefault(lang) ?? Tips.GetValueOrDefault("en") ?? Array.Empty<string>();
    public string? BenefitsText(string lang) => Benefits?.GetValueOrDefault(lang) ?? Benefits?.GetValueOrDefault("en");
    public string? CautionText(string lang) => Cautions?.GetValueOrDefault(lang) ?? Cautions?.GetValueOrDefault("en");
}

/// <summary>How long a bed should rest from a crop family. Editable content, not a fixed rotation recipe.</summary>
public sealed class RotationRule
{
    public string Family { get; set; } = "";
    public int MinYears { get; set; }
    public string Evidence { get; set; } = "recommendation";
    public Dictionary<string, string> Reason { get; set; } = new();
    public string ReasonText(string lang) => Reason.GetValueOrDefault(lang) ?? Reason.GetValueOrDefault("en") ?? "";
}

/// <summary>Why two plants are (or are not) neighbours, with the strength of the evidence behind the claim.</summary>
public sealed class PlantRelationship
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    /// <summary>positive, negative, mixed or uncertain.</summary>
    public string Effect { get; set; } = "uncertain";
    public string Category { get; set; } = "";
    /// <summary>research, recommendation, traditional or anecdotal.</summary>
    public string Evidence { get; set; } = "anecdotal";
    public Dictionary<string, string> Explanation { get; set; } = new();
    public Dictionary<string, string>? Conditions { get; set; }

    public string ExplanationText(string lang) => Explanation.GetValueOrDefault(lang) ?? Explanation.GetValueOrDefault("en") ?? "";
    public string? ConditionsText(string lang) => Conditions?.GetValueOrDefault(lang) ?? Conditions?.GetValueOrDefault("en");
    public bool Involves(string id) => A == id || B == id;
    public string Other(string id) => A == id ? B : A;
}
