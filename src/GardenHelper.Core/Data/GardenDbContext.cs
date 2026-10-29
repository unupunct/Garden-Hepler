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
        b.Entity<BookSection>().HasIndex(x => new { x.BookId, x.Ordinal });
        b.Entity<BookSubject>().HasIndex(x => x.Term);
    }
}
