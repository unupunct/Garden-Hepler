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
    public string Confidence { get; set; } = "moderate";
    public string? Reviewed { get; set; }
    public SourceRef[] Sources { get; set; } = Array.Empty<SourceRef>();
    [JsonIgnore] public bool IsUserContent { get; set; }

    public string DisplayName(string lang) =>
        (Names.GetValueOrDefault(lang)?.FirstOrDefault()) ?? Names.GetValueOrDefault("en")?.FirstOrDefault() ?? Scientific;

    public string SummaryText(string lang) => Summary.GetValueOrDefault(lang) ?? Summary.GetValueOrDefault("en") ?? "";
    public string[] TipList(string lang) => Tips.GetValueOrDefault(lang) ?? Tips.GetValueOrDefault("en") ?? Array.Empty<string>();
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
