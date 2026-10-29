using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Data;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Services;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record SearchGroup(string Title, IReadOnlyList<SearchResult> Items);

public sealed partial class SearchViewModel : PageViewModel
{
    private readonly GlobalSearchService _search;
    private readonly INavigator _nav;

    public SearchViewModel(GlobalSearchService search, INavigator nav, ILogger<SearchViewModel> log) : base(log)
    {
        _search = search; _nav = nav;
    }

    public ObservableCollection<SearchGroup> Groups { get; } = new();
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private bool _noResults;

    public override Task LoadAsync() => RunAsync(async () =>
    {
        Groups.Clear();
        var results = await _search.SearchAsync(Query, Loc.Instance.Language);
        foreach (var g in results.GroupBy(r => r.Kind).OrderBy(g => g.Key))
            Groups.Add(new SearchGroup(Loc.T("search_kind_" + g.Key), g.ToList()));
        NoResults = results.Count == 0;
    });

    [RelayCommand]
    private void Open(SearchResult r)
    {
        switch (r.Kind)
        {
            case SearchKind.Plant: _nav.GoTo<PlantsViewModel>(vm => vm.PendingSelect = r.TargetId); break;
            case SearchKind.Book:
                var term = Query;
                _nav.GoTo<ReaderViewModel>(vm => vm.Open(int.Parse(r.TargetId), r.Ordinal ?? 1, term)); break;
            case SearchKind.Journal: _nav.GoTo<JournalViewModel>(vm => vm.PendingSelect = int.Parse(r.TargetId)); break;
            case SearchKind.Seed: _nav.GoTo<SeedsViewModel>(vm => vm.PendingSelect = int.Parse(r.TargetId)); break;
            case SearchKind.Task: _nav.GoTo<TasksViewModel>(); break;
            case SearchKind.Feature: _nav.GoTo<SpaceViewModel>(vm => vm.FeatureId = int.Parse(r.TargetId)); break;
        }
    }
}

public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly Preferences _prefs;
    private readonly BackupService _backups;
    private readonly AppPaths _paths;
    private readonly IDialogs _dialogs;
    private bool _loading;

    public SettingsViewModel(Preferences prefs, BackupService backups, AppPaths paths, IDialogs dialogs, ILogger<SettingsViewModel> log) : base(log)
    {
        _prefs = prefs; _backups = backups; _paths = paths; _dialogs = dialogs;
        _loading = true;
        Language = prefs.Language; Theme = prefs.Theme; Units = prefs.Units;
        _loading = false;
    }

    public ObservableCollection<BackupInfo> Backups { get; } = new();
    public string DataFolder => _paths.Root;
    public string DatabasePath => _paths.DatabaseFile;

    [ObservableProperty] private string _language = "en";
    [ObservableProperty] private string _theme = "light";
    [ObservableProperty] private string _units = "metric";
    [ObservableProperty] private bool _restorePending;

    partial void OnLanguageChanged(string value) { if (!_loading) _ = _prefs.SetAsync(language: value); }
    partial void OnThemeChanged(string value) { if (!_loading) _ = _prefs.SetAsync(theme: value); }
    partial void OnUnitsChanged(string value) { if (!_loading) _ = _prefs.SetAsync(units: value); }

    public override Task LoadAsync() => RunAsync(() =>
    {
        RefreshBackups();
        RestorePending = File.Exists(_paths.PendingRestoreFile);
        return Task.CompletedTask;
    }, busy: false);

    private void RefreshBackups()
    {
        Backups.Clear();
        foreach (var b in _backups.List()) Backups.Add(b);
    }

    [RelayCommand]
    private Task BackupNowAsync() => RunAsync(async () =>
    {
        var info = await _backups.CreateAsync();
        RefreshBackups();
        StatusMessage = Loc.F("backup_created", Path.GetFileName(info.Path));
    });

    [RelayCommand]
    private Task VerifyAsync(BackupInfo backup) => RunAsync(async () =>
    {
        var problem = await BackupService.VerifyAsync(backup.Path);
        StatusMessage = problem is null ? Loc.T("backup_ok") : Loc.F("backup_bad", problem);
    });

    [RelayCommand]
    private Task RestoreAsync(BackupInfo backup) => RunAsync(async () =>
    {
        if (!_dialogs.Confirm(Loc.T("restore_confirm"))) return;
        await _backups.ScheduleRestoreAsync(backup.Path);
        RestorePending = true;
        StatusMessage = Loc.T("restore_scheduled");
    });

    [RelayCommand] private void OpenData() => _dialogs.OpenFolder(_paths.Root);
    [RelayCommand] private void OpenBackups() => _dialogs.OpenFolder(_paths.BackupDir);
    [RelayCommand] private void OpenContent() => _dialogs.OpenFolder(_paths.UserContentDir);
}

public sealed record Notice(string Name, string License, string Use);

public sealed partial class AboutViewModel : PageViewModel
{
    private readonly IDialogs _dialogs;
    private readonly KnowledgeBase _kb;
    public AboutViewModel(IDialogs dialogs, KnowledgeBase kb, AppPaths paths) : base(null)
    {
        _dialogs = dialogs; _kb = kb; DataFolder = paths.Root;
    }

    public const string Repository = "https://github.com/unupunct/Garden-Hepler";
    public string Version => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.1.0";
    public string DataFolder { get; }
    public string ContentSummary => Loc.F("about_content", _kb.Plants.Count, _kb.Relationships.Count);
    public IReadOnlyList<Notice> Notices { get; } = new[]
    {
        new Notice(".NET / WPF", "MIT", "Runtime and UI framework"),
        new Notice("CommunityToolkit.Mvvm", "MIT", "MVVM helpers"),
        new Notice("Entity Framework Core + SQLite", "MIT / public domain", "Local database and full-text search"),
        new Notice("PdfPig", "Apache-2.0", "Reading text from PDF files"),
        new Notice("HtmlAgilityPack", "MIT", "Reading text from HTML/EPUB"),
        new Notice("Windows PDF engine (Windows.Data.Pdf)", "Part of Windows", "Showing PDF pages in the reader"),
        new Notice("Serilog", "Apache-2.0", "Local log files"),
    };

    [RelayCommand] private void OpenRepo() => _dialogs.OpenUrl(Repository);
    [RelayCommand] private void OpenReleases() => _dialogs.OpenUrl(Repository + "/releases");
    [RelayCommand] private void OpenData() => _dialogs.OpenFolder(DataFolder);
}
