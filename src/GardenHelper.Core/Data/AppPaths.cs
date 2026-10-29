namespace GardenHelper.Core.Data;

/// <summary>Locations of all user data. Override the root with GARDENHELPER_DATA (used by tests).</summary>
public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root
            ?? Environment.GetEnvironmentVariable("GARDENHELPER_DATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GardenHelper");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }
    public string DatabaseFile => Path.Combine(Root, "gardenhelper.db");
    public string PendingRestoreFile => Path.Combine(Root, "restore.pending");
    public string LibraryDir => Ensure(Path.Combine(Root, "library"));
    public string ReaderCacheDir => Ensure(Path.Combine(Root, "reader-cache"));
    public string BackupDir => Ensure(Path.Combine(Root, "backups"));
    public string LogDir => Ensure(Path.Combine(Root, "logs"));
    public string UserContentDir => Ensure(Path.Combine(Root, "content"));
    public string RecordingsDir => Ensure(Path.Combine(Root, "recordings"));
    public string SeedPhotosDir => Ensure(Path.Combine(Root, "seed-photos"));
    public string SiteDir => Ensure(Path.Combine(Root, "site"));
    public string WebViewDataDir => Ensure(Path.Combine(Root, "webview2"));

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
