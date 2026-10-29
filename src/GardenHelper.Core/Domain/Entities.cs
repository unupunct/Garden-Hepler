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
