using GardenHelper.Core.Data;
using GardenHelper.Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GardenHelper.Core.Services;

/// <summary>Small generic repository: every call uses its own short-lived context.</summary>
public sealed class Repository
{
    private readonly IDbContextFactory<GardenDbContext> _factory;
    public Repository(IDbContextFactory<GardenDbContext> factory) => _factory = factory;

    public async Task<List<T>> ListAsync<T>(Func<IQueryable<T>, IQueryable<T>>? shape = null, CancellationToken ct = default) where T : class
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        IQueryable<T> q = db.Set<T>().AsNoTracking();
        if (shape is not null) q = shape(q);
        return await q.ToListAsync(ct);
    }

    public async Task<T> SaveAsync<T>(T item, CancellationToken ct = default) where T : class
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var key = db.Entry(item).Property("Id").CurrentValue;
        db.Entry(item).State = key is int id && id == 0 ? EntityState.Added : EntityState.Modified;
        await db.SaveChangesAsync(ct);
        return item;
    }

    public async Task DeleteAsync<T>(T item, CancellationToken ct = default) where T : class
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Entry(item).State = EntityState.Deleted;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> CountAsync<T>(Func<IQueryable<T>, IQueryable<T>>? shape = null, CancellationToken ct = default) where T : class
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        IQueryable<T> q = db.Set<T>();
        if (shape is not null) q = shape(q);
        return await q.CountAsync(ct);
    }

    public async Task<Garden?> ActiveGardenAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Gardens.AsNoTracking().Where(g => g.IsActive).OrderBy(g => g.Id).FirstOrDefaultAsync(ct);
    }
}

public sealed class SettingsService
{
    private readonly IDbContextFactory<GardenDbContext> _factory;
    public SettingsService(IDbContextFactory<GardenDbContext> factory) => _factory = factory;

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return (await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct))?.Value;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var s = await db.Settings.FindAsync(new object[] { key }, ct);
        if (s is null) db.Settings.Add(new AppSetting { Key = key, Value = value }); else s.Value = value;
        await db.SaveChangesAsync(ct);
    }
}

public sealed record BackupInfo(string Path, DateTime Created, long SizeBytes);

public sealed class BackupService
{
    private readonly AppPaths _paths;
    private readonly ILogger<BackupService> _log;
    public BackupService(AppPaths paths, ILogger<BackupService> log) { _paths = paths; _log = log; }

    /// <summary>Writes a consistent copy of the database (VACUUM INTO) and verifies it before reporting success.</summary>
    public async Task<BackupInfo> CreateAsync(string? reason = null, CancellationToken ct = default)
    {
        var name = $"gardenhelper-{DateTime.Now:yyyyMMdd-HHmmss}{(reason is null ? "" : "-" + reason)}.db";
        var target = Path.Combine(_paths.BackupDir, name);
        await using (var conn = new SqliteConnection($"Data Source={_paths.DatabaseFile};Pooling=False"))
        {
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO $path";
            cmd.Parameters.AddWithValue("$path", target);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        var problem = await VerifyAsync(target, ct);
        if (problem is not null)
        {
            File.Delete(target);
            throw new InvalidOperationException("The backup failed verification: " + problem);
        }
        _log.LogInformation("Backup created: {File}", target);
        return new BackupInfo(target, File.GetCreationTime(target), new FileInfo(target).Length);
    }

    /// <summary>Returns null when the file is a healthy Garden Helper database, otherwise a description of the problem.</summary>
    public static async Task<string?> VerifyAsync(string file, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check";
            var result = (string?)await cmd.ExecuteScalarAsync(ct);
            if (result != "ok") return "integrity check reported: " + result;
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('Books','Gardens','__EFMigrationsHistory')";
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) < 3) return "this is not a Garden Helper database.";
            return null;
        }
        catch (SqliteException ex) { return ex.Message; }
    }

    public List<BackupInfo> List() =>
        Directory.EnumerateFiles(_paths.BackupDir, "*.db").Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime).Select(f => new BackupInfo(f.FullName, f.CreationTime, f.Length)).ToList();

    /// <summary>Marks a backup to be restored on next start. The running database is never overwritten in place.</summary>
    public async Task ScheduleRestoreAsync(string file, CancellationToken ct = default)
    {
        var problem = await VerifyAsync(file, ct);
        if (problem is not null) throw new InvalidOperationException("This file cannot be restored: " + problem);
        await File.WriteAllTextAsync(_paths.PendingRestoreFile, file, ct);
    }

    public async Task<BackupInfo?> AutoBackupIfDueAsync(TimeSpan interval, CancellationToken ct = default)
    {
        var last = List().FirstOrDefault();
        if (last is not null && DateTime.Now - last.Created < interval) return null;
        var info = await CreateAsync("auto", ct);
        // Keep the 10 newest automatic backups.
        foreach (var old in Directory.EnumerateFiles(_paths.BackupDir, "*-auto.db").OrderByDescending(f => f).Skip(10))
            try { File.Delete(old); } catch (IOException) { }
        return info;
    }
}
