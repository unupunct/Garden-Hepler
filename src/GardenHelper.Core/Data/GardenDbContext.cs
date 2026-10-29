using GardenHelper.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace GardenHelper.Core.Data;

public class GardenDbContext : DbContext
{
    public GardenDbContext(DbContextOptions<GardenDbContext> options) : base(options) { }

    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<Garden> Gardens => Set<Garden>();
    public DbSet<JournalEntry> Journal => Set<JournalEntry>();
    public DbSet<SeedItem> Seeds => Set<SeedItem>();
    public DbSet<GardenTask> Tasks => Set<GardenTask>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookSection> BookSections => Set<BookSection>();
    public DbSet<BookSubject> BookSubjects => Set<BookSubject>();
    public DbSet<GardenBed> Beds => Set<GardenBed>();
    public DbSet<BedPlanting> Plantings => Set<BedPlanting>();
    public DbSet<PropertyFeature> Features => Set<PropertyFeature>();
    public DbSet<FeatureLog> FeatureLogs => Set<FeatureLog>();
    public DbSet<InventoryPlant> Inventory => Set<InventoryPlant>();
    public DbSet<Cameras.Camera> Cameras => Set<Cameras.Camera>();
    public DbSet<Cameras.CameraEvent> CameraEvents => Set<Cameras.CameraEvent>();
    public DbSet<Greenhouse.GreenhouseSetup> GreenhouseSetups => Set<Greenhouse.GreenhouseSetup>();
    public DbSet<Greenhouse.GreenhouseReading> GreenhouseReadings => Set<Greenhouse.GreenhouseReading>();
    public DbSet<Greenhouse.GreenhouseActuator> GreenhouseActuators => Set<Greenhouse.GreenhouseActuator>();
    public DbSet<Greenhouse.GreenhouseItem> GreenhouseItems => Set<Greenhouse.GreenhouseItem>();
    public DbSet<Planning.BudgetEntry> Budget => Set<Planning.BudgetEntry>();
    public DbSet<Animals.AnimalGroup> AnimalGroups => Set<Animals.AnimalGroup>();
    public DbSet<Animals.AnimalLog> AnimalLogs => Set<Animals.AnimalLog>();
    public DbSet<Animals.FeedRation> FeedRations => Set<Animals.FeedRation>();
    public DbSet<InventoryLog> InventoryLogs => Set<InventoryLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppSetting>().HasKey(x => x.Key);

        b.Entity<JournalEntry>().HasIndex(x => x.Date);
        b.Entity<GardenTask>().HasIndex(x => new { x.Done, x.DueDate });
        b.Entity<SeedItem>().HasIndex(x => x.PlantId);

        b.Entity<Book>(e =>
        {
            e.HasIndex(x => x.Sha256).IsUnique();
            e.HasMany(x => x.Sections).WithOne(x => x.Book).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Subjects).WithOne(x => x.Book).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<GardenBed>(e =>
        {
            e.HasIndex(x => x.GardenId);
            e.HasMany(x => x.Plantings).WithOne(x => x.Bed).HasForeignKey(x => x.BedId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<BedPlanting>().HasIndex(x => new { x.BedId, x.Status });
        b.Entity<PropertyFeature>(e =>
        {
            e.HasIndex(x => x.GardenId); e.HasIndex(x => x.Name);
            e.HasMany(x => x.Logs).WithOne(x => x.Feature).HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<FeatureLog>().HasIndex(x => new { x.FeatureId, x.Date });
        b.Entity<InventoryPlant>(e =>
        {
            e.HasIndex(x => x.Name); e.HasIndex(x => x.Location);
            e.HasMany(x => x.Logs).WithOne(x => x.Plant).HasForeignKey(x => x.PlantId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<InventoryLog>().HasIndex(x => new { x.PlantId, x.Date });
        b.Entity<Animals.AnimalGroup>(e =>
        {
            e.HasIndex(x => x.GardenId);
            e.HasMany(x => x.Rations).WithOne(x => x.Group).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Logs).WithOne(x => x.Group).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Planning.BudgetEntry>().HasIndex(x => x.Date);
        b.Entity<Greenhouse.GreenhouseSetup>().HasIndex(x => x.FeatureId).IsUnique();
        b.Entity<Greenhouse.GreenhouseReading>().HasIndex(x => new { x.FeatureId, x.Metric, x.AtUtc });
        b.Entity<Greenhouse.GreenhouseActuator>().HasIndex(x => new { x.FeatureId, x.Key }).IsUnique();
        b.Entity<Greenhouse.GreenhouseItem>().HasIndex(x => x.FeatureId);
        b.Entity<Animals.AnimalLog>().HasIndex(x => new { x.GroupId, x.Date });
        b.Entity<Cameras.Camera>().HasIndex(x => x.GardenId);
        b.Entity<Cameras.CameraEvent>().HasIndex(x => new { x.CameraId, x.AtUtc });
        b.Entity<BookSection>().HasIndex(x => new { x.BookId, x.Ordinal });
        b.Entity<BookSubject>().HasIndex(x => x.Term);
    }
}
