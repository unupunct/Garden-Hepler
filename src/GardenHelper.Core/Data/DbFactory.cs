using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GardenHelper.Core.Data;

public static class DbFactory
{
    public static DbContextOptions<GardenDbContext> Options(string dbFile) =>
        new DbContextOptionsBuilder<GardenDbContext>()
            .UseSqlite($"Data Source={dbFile};Foreign Keys=True")
            .Options;

    /// <summary>Applies a pending restore (if any), then migrates the database to the latest schema.</summary>
    public static async Task InitializeAsync(AppPaths paths, CancellationToken ct = default)
    {
        ApplyPendingRestore(paths);
        await using var db = new GardenDbContext(Options(paths.DatabaseFile));
        await db.Database.MigrateAsync(ct);
    }

    public static void ApplyPendingRestore(AppPaths paths)
    {
        if (!File.Exists(paths.PendingRestoreFile)) return;
        var source = File.ReadAllText(paths.PendingRestoreFile).Trim();
        File.Delete(paths.PendingRestoreFile);
        if (!File.Exists(source)) return;
        if (File.Exists(paths.DatabaseFile))
        {
            var safety = Path.Combine(paths.BackupDir, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            File.Move(paths.DatabaseFile, safety, true);
        }
        foreach (var ext in new[] { "-wal", "-shm" })
            if (File.Exists(paths.DatabaseFile + ext)) File.Delete(paths.DatabaseFile + ext);
        File.Copy(source, paths.DatabaseFile, true);
    }
}

/// <summary>Used only by "dotnet ef" at design time.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<GardenDbContext>
{
    public GardenDbContext CreateDbContext(string[] args) =>
        new(DbFactory.Options(Path.Combine(Path.GetTempPath(), "gardenhelper-design.db")));
}
