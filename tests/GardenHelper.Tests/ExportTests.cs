using System.IO.Compression;
using GardenHelper.Core.Data;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GardenHelper.Tests;

public class ExportTests
{
    [Fact]
    public async Task Export_then_import_into_a_fresh_folder_brings_back_the_data_and_the_pictures()
    {
        using var source = await TestEnv.CreateAsync();
        await new Repository(source.Factory).SaveAsync(new JournalEntry { Title = "kept in the export" });
        await File.WriteAllBytesAsync(Path.Combine(source.Paths.SeedPhotosDir, "pic.jpg"), new byte[] { 0xFF, 0xD8, 0xFF, 1, 2, 3 });
        var zip = Path.Combine(source.Root, "export.zip");
        var export = new ExportService(source.Paths, new BackupService(source.Paths, NullLogger<BackupService>.Instance));
        var info = await export.ExportAsync(zip, includeLibrary: false);
        Assert.True(info.Files >= 3);

        using var target = await TestEnv.CreateAsync();
        var import = new ExportService(target.Paths, new BackupService(target.Paths, NullLogger<BackupService>.Instance));
        await import.ImportAsync(zip);
        Assert.True(File.Exists(Path.Combine(target.Paths.SeedPhotosDir, "pic.jpg")));
        SqliteConnection.ClearAllPools();
        DbFactory.ApplyPendingRestore(target.Paths);
        Assert.Equal("kept in the export", Assert.Single(await new Repository(target.Factory).ListAsync<JournalEntry>()).Title);
    }

    [Fact]
    public async Task A_zip_that_is_not_an_export_or_tries_to_escape_is_refused_or_ignored()
    {
        using var env = await TestEnv.CreateAsync();
        var svc = new ExportService(env.Paths, new BackupService(env.Paths, NullLogger<BackupService>.Instance));
        var bad = Path.Combine(env.Root, "bad.zip");
        using (var z = ZipFile.Open(bad, ZipArchiveMode.Create)) z.CreateEntry("hello.txt");
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ImportAsync(bad));
    }
}
