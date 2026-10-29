namespace GardenHelper.Core.Domain;

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class Garden
{
    public int Id { get; set; }
    public string Name { get; set; } = "My garden";
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? HardinessZone { get; set; }
    /// <summary>Typical last spring frost. Only month and day are used; the year is ignored.</summary>
    public DateOnly? LastSpringFrost { get; set; }
    /// <summary>Typical first autumn frost. Only month and day are used; the year is ignored.</summary>
    public DateOnly? FirstAutumnFrost { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    /// <summary>Serialized ClimateSummary from the last (opt-in) online fetch.</summary>
    public string? ClimateJson { get; set; }
    /// <summary>Satellite picture saved in the data folder (file name only).</summary>
    public string? SatelliteFile { get; set; }
    public double? SatelliteCmPerPixel { get; set; }
    /// <summary>Property outline as JSON [{Lat,Lon},…] (WGS84), 3–20 pins placed on the map by the user.</summary>
    public string? BoundaryJson { get; set; }
    /// <summary>Satellite picture stitched for the designer (file name in the site folder) and where it sits on the map.</summary>
    public string? BackdropFile { get; set; }
    public double? BackdropTopLat { get; set; }
    public double? BackdropTopLon { get; set; }
    public double? BackdropCmPerPixel { get; set; }
    /// <summary>The spot on the ground (WGS84) that was at the top-left corner (0,0) of the garden plan when the plan was last opened. It lets the plan stay put on the ground when places are traced, reshaped or deleted.</summary>
    public double? PlanAnchorLat { get; set; }
    public double? PlanAnchorLon { get; set; }
}

/// <summary>A note or line drawn on the garden plan: a text label, an irrigation line, a path or a cable run. Points are plan coordinates in centimetres, as JSON [{X,Y},…].</summary>
public class PlanMark
{
    public int Id { get; set; }
    public int GardenId { get; set; }
    /// <summary>label, irrigation, path or cable.</summary>
    public string Kind { get; set; } = "label";
    public string Text { get; set; } = "";
    public string PointsJson { get; set; } = "[]";
}

/// <summary>One harvest: when, what, from which bed, and how much (optional).</summary>
public class Harvest
{
    public int Id { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string PlantId { get; set; } = "";
    public int? BedId { get; set; }
    /// <summary>Amount in <see cref="Unit"/>; empty when only the fact of harvesting was noted.</summary>
    public double? Quantity { get; set; }
    /// <summary>kg or pcs.</summary>
    public string Unit { get; set; } = "kg";
    public string? Notes { get; set; }
}

/// <summary>A picture taken on the property: of a bed, a plant, an animal group, a place, or just the garden. The file is kept in the photo folder.</summary>
public class Photo
{
    public int Id { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    /// <summary>Stored file name (see SeedPhotoStore).</summary>
    public string File { get; set; } = "";
    public string? Caption { get; set; }
    /// <summary>general, bed, animal, place or plant.</summary>
    public string OwnerKind { get; set; } = "general";
    public int? OwnerId { get; set; }
    public string? PlantId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A named place traced on the satellite map: a permanent structure (house, workshop…) or a garden space (garden 1, greenhouse, compost pile…).</summary>
public class PropertyFeature
{
    public int Id { get; set; }
    public int GardenId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>A key from <see cref="FeatureCatalog"/>.</summary>
    public string Kind { get; set; } = "garden";
    /// <summary>Outline as JSON [{Lat,Lon},…] (WGS84).</summary>
    public string PointsJson { get; set; } = "[]";
    public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<FeatureLog> Logs { get; set; } = new();
    public override string ToString() => Name;
}

/// <summary>A dated entry in a place's history: note, growth observation, soil or plant treatment.</summary>
public class FeatureLog
{
    public int Id { get; set; }
    public int FeatureId { get; set; }
    public PropertyFeature? Feature { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    /// <summary>note, growth, soil, plant, observation.</summary>
    public string Kind { get; set; } = "note";
    public string Text { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public static class FeatureCatalog
{
    public static readonly string[] Structures = { "house", "workshop", "shed", "barn", "garage", "coop", "pen", "other_structure" };
    public static readonly string[] Spaces = { "garden", "greenhouse", "compost", "orchard", "pond", "free_land", "other_space" };
    public static readonly string[] LogKinds = { "note", "growth", "soil", "plant", "observation", "maintenance" };

    /// <summary>Which kinds of log entry make sense for a place: growing spaces track growth and treatments, buildings track maintenance.</summary>
    public static string[] LogKindsFor(string kind) =>
        IsStructure(kind) ? new[] { "note", "maintenance", "observation" }
        : kind == "compost" ? new[] { "note", "observation", "soil" }
        : kind is "pond" ? new[] { "note", "observation", "maintenance" }
        : new[] { "note", "growth", "soil", "plant", "observation" };

    /// <summary>Spaces where crops are planned and tracked.</summary>
    public static bool HoldsPlantings(string kind) => kind is "garden" or "greenhouse" or "orchard" or "other_space" or "free_land";
    public static bool IsStructure(string kind) => Structures.Contains(kind);
}

public enum PlantLocation { Outdoor = 0, Greenhouse = 1, Indoor = 2 }

/// <summary>A plant (or group of identical plants) you own: outdoors, in the greenhouse, or indoors in a pot.</summary>
public class InventoryPlant
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Id of a record in the plant encyclopedia, when the plant is one of them. Custom plants leave it empty.</summary>
    public string? SpeciesId { get; set; }
    public string? Variety { get; set; }
    public PlantLocation Location { get; set; }
    /// <summary>Where it is, in your words: "kitchen window", "Garden 1, row 3", "greenhouse bench B".</summary>
    public string? Place { get; set; }
    public int Quantity { get; set; } = 1;
    public DateOnly? Acquired { get; set; }
    public string? Source { get; set; }
    /// <summary>healthy, attention, recovering or dead.</summary>
    public string Status { get; set; } = "healthy";
    public string? Notes { get; set; }
    public int? WateringIntervalDays { get; set; }
    public DateOnly? LastWatered { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Trees are kept in the same inventory; the fields below are only used for them.</summary>
    public bool IsTree { get; set; }
    /// <summary>fruit, nut, shade, conifer or other.</summary>
    public string? TreeType { get; set; }
    public DateOnly? PlantedOn { get; set; }
    public double? HeightCm { get; set; }
    public double? CrownRadiusCm { get; set; }
    public double? RootRadiusCm { get; set; }
    /// <summary>The tree object on the garden plan, so shade and roots can be worked out against the beds.</summary>
    public int? BedId { get; set; }
    public List<InventoryLog> Logs { get; set; } = new();
    public override string ToString() => Name;
}

public class InventoryLog
{
    public int Id { get; set; }
    public int PlantId { get; set; }
    public InventoryPlant? Plant { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    /// <summary>note, watering, feeding, repotting, pruning, treatment, observation.</summary>
    public string Kind { get; set; } = "note";
    public string Text { get; set; } = "";
}

public static class InventoryRules
{
    public static readonly string[] Statuses = { "healthy", "attention", "recovering", "dead" };
    public static readonly string[] LogKinds = { "note", "watering", "feeding", "repotting", "pruning", "treatment", "observation" };

    /// <summary>The date the next watering is due, or null when no interval is set. Never watered counts from the day it was added.</summary>
    public static DateOnly? NextWatering(InventoryPlant p) =>
        p.WateringIntervalDays is > 0 and var days && p.Status != "dead"
            ? (p.LastWatered ?? DateOnly.FromDateTime(p.CreatedUtc.ToLocalTime())).AddDays(days) : null;

    /// <summary>Days overdue (positive), due today (0) or days remaining (negative); null when there is no schedule.</summary>
    public static int? DaysOverdue(InventoryPlant p, DateOnly today) => NextWatering(p) is { } d ? today.DayNumber - d.DayNumber : null;
}

public enum PlantingStatus { Planned = 0, Sown = 1, Growing = 2, Harvesting = 3, Finished = 4 }

/// <summary>An object on the garden plan. Sizes are centimetres; X/Y is the top-left corner.</summary>
public class GardenBed
{
    public int Id { get; set; }
    public int GardenId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>bed, raised, greenhouse, pond, compost, tree, path, structure.</summary>
    public string Kind { get; set; } = "bed";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 300;
    /// <summary>Clockwise rotation in degrees about the object's centre.</summary>
    public double Rotation { get; set; }
    /// <summary>A locked bed cannot be moved or resized by accident.</summary>
    public bool Locked { get; set; }
    /// <summary>The map feature (garden space) this bed was planned from, if any.</summary>
    public int? FeatureId { get; set; }
    /// <summary>The place whose sensor readings (soil moisture, temperature) belong to this bed.</summary>
    public int? SensorFeatureId { get; set; }
    /// <summary>Share of the day the spot is shaded by buildings or trees, 0-90 (user estimate).</summary>
    public int ShadePercent { get; set; }
    public string? Notes { get; set; }
    public List<BedPlanting> Plantings { get; set; } = new();
}

public class BedPlanting
{
    public int Id { get; set; }
    public int BedId { get; set; }
    public GardenBed? Bed { get; set; }
    public string PlantId { get; set; } = "";
    public string? Variety { get; set; }
    public int Quantity { get; set; } = 1;
    public DateOnly? PlantedDate { get; set; }
    public PlantingStatus Status { get; set; } = PlantingStatus.Planned;
    public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class JournalEntry
{
    public int Id { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string Tags { get; set; } = "";
    public string? PlantId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public override string ToString() => Title;
}

public class SeedItem
{
    public int Id { get; set; }
    public string? PlantId { get; set; }
    public string Name { get; set; } = "";
    public string? Variety { get; set; }
    public string? Supplier { get; set; }
    public string? Lot { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal QuantityRemaining { get; set; }
    public string Unit { get; set; } = "seeds";
    public string? StorageLocation { get; set; }
    public DateOnly? ReviewDate { get; set; }
    public decimal? GerminationPercent { get; set; }
    public decimal? Cost { get; set; }
    public string? Notes { get; set; }
    /// <summary>File names (inside the seed-photos folder) of pictures of the packet: front, and back with the sowing instructions.</summary>
    public string? FrontPhoto { get; set; }
    public string? BackPhoto { get; set; }
}

public class GardenTask
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Activity { get; set; } = "General";
    public DateOnly DueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public bool Done { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string? PlantId { get; set; }
    public string? Notes { get; set; }
}

public enum IndexStatus { Pending = 0, Indexed = 1, NoText = 2, Failed = 3 }

public class Book
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string? Language { get; set; }
    public string? Description { get; set; }
    public string Format { get; set; } = "";
    /// <summary>Path of the managed copy, relative to the library folder.</summary>
    public string StoredFile { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
    public int SectionCount { get; set; }
    public int WordCount { get; set; }
    public IndexStatus IndexStatus { get; set; }
    public string? IndexMessage { get; set; }
    public string Tags { get; set; } = "";
    public int LastReadSection { get; set; }
    public DateTime? LastReadUtc { get; set; }
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public List<BookSection> Sections { get; set; } = new();
    public List<BookSubject> Subjects { get; set; } = new();
}

public class BookSection
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public Book? Book { get; set; }
    public int Ordinal { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>For formats that are displayed from their own files (EPUB, HTML): path inside the book.</summary>
    public string? ContentPath { get; set; }
}

public class BookSubject
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public Book? Book { get; set; }
    public string Term { get; set; } = "";
    public double Weight { get; set; }
    /// <summary>metadata, heading or frequency.</summary>
    public string Source { get; set; } = "frequency";
}
