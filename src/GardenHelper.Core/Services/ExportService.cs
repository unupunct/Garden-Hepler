using System.IO.Compression;
using System.Text.Json;
using GardenHelper.Core.Data;

namespace GardenHelper.Core.Services;

public sealed record ExportInfo(string Path, long Bytes, int Files, bool IncludesLibrary);

/// <summary>
/// Everything in one ZIP, to move to a new computer or keep safe: the database (verified), photos, satellite pictures, your own content and, if you choose, your book library.
/// Importing puts the files back and schedules the database to be restored at the next start.
/// </summary>
public sealed class ExportService
{
    private const string Manifest = "manifest.json", DbEntry = "gardenhelper.db";
    private readonly AppPaths _paths;
    private readonly BackupService _backups;
    public ExportService(AppPaths paths, BackupService backups) { _paths = paths; _backups = backups; }

    private IEnumerable<(string Folder, string Dir)> Folders(bool library)
    {
        yield return ("seed-photos", _paths.SeedPhotosDir);
        yield return ("site", _paths.SiteDir);
        yield return ("content", _paths.UserContentDir);
        if (library) yield return ("library", _paths.LibraryDir);
    }

    public async Task<ExportInfo> ExportAsync(string zipPath, bool includeLibrary, CancellationToken ct = default)
    {
        var backup = await _backups.CreateAsync("export", ct);
        try
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            var files = 0;
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(backup.Path, DbEntry, CompressionLevel.Optimal); files++;
                foreach (var (folder, dir) in Folders(includeLibrary))
                    foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        var entry = folder + "/" + Path.GetRelativePath(dir, file).Replace('\\', '/');
                        zip.CreateEntryFromFile(file, entry, CompressionLevel.Fastest); files++;
                    }
                var manifest = zip.CreateEntry(Manifest); files++;
                await using (var w = manifest.Open())
                    await JsonSerializer.SerializeAsync(w, new { app = "GardenHelper", format = 1, createdUtc = DateTime.UtcNow, library = includeLibrary }, cancellationToken: ct);
            }
            return new ExportInfo(zipPath, new FileInfo(zipPath).Length, files, includeLibrary);
        }
        finally { try { File.Delete(backup.Path); } catch (IOException) { } }
    }

    /// <summary>Checks the ZIP, copies its files into the data folder, and schedules its database for the next start. Throws with a clear message when it is not an export of this app.</summary>
    public async Task ImportAsync(string zipPath, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.GetEntry(Manifest) is not { } m || zip.GetEntry(DbEntry) is not { } db) throw new InvalidOperationException("This is not a Garden Helper export.");
        await using (var s = m.Open())
        {
            using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("app", out var app) || app.GetString() != "GardenHelper") throw new InvalidOperationException("This is not a Garden Helper export.");
        }
        var staged = Path.Combine(_paths.BackupDir, $"gardenhelper-{DateTime.Now:yyyyMMdd-HHmmss}-import.db");
        db.ExtractToFile(staged, true);
        var problem = await BackupService.VerifyAsync(staged, ct);
        if (problem is not null) { File.Delete(staged); throw new InvalidOperationException("The database in this export is damaged: " + problem); }

        var roots = Folders(true).ToDictionary(f => f.Folder, f => Path.GetFullPath(f.Dir));
        foreach (var entry in zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name) && e.FullName != DbEntry && e.FullName != Manifest))
        {
            ct.ThrowIfCancellationRequested();
            var slash = entry.FullName.IndexOf('/');
            if (slash < 0 || !roots.TryGetValue(entry.FullName[..slash], out var root)) continue;
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName[(slash + 1)..].Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;      // never write outside the data folders
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
        await _backups.ScheduleRestoreAsync(staged, ct);
    }
}
