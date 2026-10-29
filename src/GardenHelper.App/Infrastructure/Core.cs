using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using GardenHelper.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GardenHelper.App.Infrastructure;

public interface INavigator
{
    void GoTo<T>(Action<T>? init = null) where T : PageViewModel;
}

public interface IDialogs
{
    string[]? PickBooks(string filter);
    string? PickFile(string filter);
    string? PickFolder(string? title = null);
    bool Confirm(string message, string? title = null);
    void Info(string message, string? title = null);
    void OpenFolder(string path);
    void OpenUrl(string url);
}

public sealed class WpfDialogs : IDialogs
{
    public string[]? PickBooks(string filter)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = filter, CheckFileExists = true, Title = Loc.T("lib_import") };
        return dlg.ShowDialog() == true ? dlg.FileNames : null;
    }

    public string? PickFile(string filter)
    {
        var dlg = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? PickFolder(string? title = null)
    {
        var dlg = new OpenFolderDialog { Title = title ?? Loc.T("lib_import_folder") };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public bool Confirm(string message, string? title = null) =>
        MessageBox.Show(Application.Current.MainWindow, message, title ?? "Garden Helper", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Info(string message, string? title = null) =>
        MessageBox.Show(Application.Current.MainWindow, message, title ?? "Garden Helper", MessageBoxButton.OK, MessageBoxImage.Information);

    public void OpenFolder(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    public void OpenUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "https" or "http")
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
    }
}

/// <summary>Base class for pages: busy flag, error banner, and a safe wrapper for async work.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    private readonly ILogger? _log;

    protected PageViewModel(ILogger? log = null) => _log = log;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;

    public virtual Task LoadAsync() => Task.CompletedTask;

    /// <summary>Runs work and turns any exception into a visible, logged message instead of a crash.</summary>
    protected async Task RunAsync(Func<Task> work, bool busy = true)
    {
        ErrorMessage = null;
        if (busy) IsBusy = true;
        try { await work(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Operation failed in {Page}", GetType().Name);
            ErrorMessage = Loc.F("err_generic", ex.Message);
        }
        finally { if (busy) IsBusy = false; }
    }
}

/// <summary>Language, theme and units, persisted in the database settings table.</summary>
public sealed class Preferences
{
    private readonly SettingsService _settings;
    public Preferences(SettingsService settings) => _settings = settings;

    public string Language { get; private set; } = "en";
    public string Theme { get; private set; } = "light";
    public string Units { get; private set; } = "metric";

    public async Task LoadAsync()
    {
        Language = await _settings.GetAsync("language") ?? "en";
        Theme = await _settings.GetAsync("theme") ?? "light";
        Units = await _settings.GetAsync("units") ?? "metric";
        Apply();
    }

    public async Task SetAsync(string? language = null, string? theme = null, string? units = null)
    {
        if (language is not null) { Language = language; await _settings.SetAsync("language", language); }
        if (theme is not null) { Theme = theme; await _settings.SetAsync("theme", theme); }
        if (units is not null) { Units = units; await _settings.SetAsync("units", units); }
        Apply();
    }

    private void Apply()
    {
        Loc.Instance.SetLanguage(Language);
        UnitFormatter.Imperial = Units == "imperial";
        ThemeManager.Apply(Theme);
    }
}

public static class ThemeManager
{
    private const string Prefix = "pack://application:,,,/GardenHelper;component/Themes/";

    public static void Apply(string theme)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        var uri = new Uri(Prefix + (theme == "dark" ? "Dark.xaml" : "Light.xaml"));
        var existing = dicts.FirstOrDefault(d => d.Source?.OriginalString.EndsWith("Light.xaml") == true || d.Source?.OriginalString.EndsWith("Dark.xaml") == true);
        var fresh = new ResourceDictionary { Source = uri };
        if (existing is not null) dicts[dicts.IndexOf(existing)] = fresh; else dicts.Insert(0, fresh);
    }
}
