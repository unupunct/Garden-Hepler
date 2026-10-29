using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GardenHelper.App.ViewModels;

public sealed partial class NavEntry : ObservableObject
{
    public NavEntry(string key, string glyph, Type? page)
    {
        Key = key; Glyph = glyph; Page = page;
        Loc.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Title));
    }
    public string Key { get; }
    public string Glyph { get; }
    public Type? Page { get; }
    public bool IsHeader => Page is null;
    public string Title => Loc.T("nav_" + Key);
    public override string ToString() => Title;   // accessible name for screen readers
}

public sealed partial class ShellViewModel : ObservableObject, INavigator
{
    private readonly IServiceProvider _services;
    private bool _navigating;

    private readonly Preferences _prefs;
    [ObservableProperty] private string _language = "en";

    partial void OnLanguageChanged(string value)
    {
        if (value != _prefs.Language) _ = _prefs.SetAsync(language: value);
    }

    public ShellViewModel(IServiceProvider services, Preferences prefs)
    {
        _services = services; _prefs = prefs;
        _language = prefs.Language;
        // Stay in sync when the language is changed from the Settings page.
        Loc.Instance.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Loc.Language) && Language != Loc.Instance.Language) Language = Loc.Instance.Language; };
        Nav = new ObservableCollection<NavEntry>
        {
            new("home", "", null),
            new("property", "⌖", typeof(PropertyViewModel)),
            new("dashboard", "⌂", typeof(DashboardViewModel)),
            new("tasks", "✔", typeof(TasksViewModel)),
            new("monitor", "◔", typeof(MonitorViewModel)),
            new("plan", "", null),
            new("designer", "▦", typeof(DesignerViewModel)),
            new("site", "☀", typeof(SiteViewModel)),
            new("map", "◎", typeof(MapViewModel)),
            new("cameras", "◉", typeof(CamerasViewModel)),
            new("animals", "♞", typeof(AnimalsViewModel)),
            new("grow", "", null),
            new("plants", "☘", typeof(PlantsViewModel)),
            new("seeds", "◉", typeof(SeedsViewModel)),
            new("journal", "✎", typeof(JournalViewModel)),
            new("learn", "", null),
            new("library", "▤", typeof(LibraryViewModel)),
            new("system", "", null),
            new("garden", "⚑", typeof(GardenViewModel)),
            new("settings", "⚙", typeof(SettingsViewModel)),
            new("about", "ℹ", typeof(AboutViewModel)),
        };
    }

    public ObservableCollection<NavEntry> Nav { get; }

    [ObservableProperty] private PageViewModel? _current;
    [ObservableProperty] private NavEntry? _selectedNav;
    [ObservableProperty] private string _searchText = "";

    /// <summary>The property command center is the home screen.</summary>
    public void Start() => GoTo<PropertyViewModel>();

    partial void OnSelectedNavChanged(NavEntry? value)
    {
        if (_navigating || value?.Page is null) return;
        Navigate(value.Page, null);
    }

    public void GoTo<T>(Action<T>? init = null) where T : PageViewModel =>
        Navigate(typeof(T), vm => init?.Invoke((T)vm));

    private void Navigate(Type page, Action<PageViewModel>? init)
    {
        var vm = (PageViewModel)_services.GetRequiredService(page);
        init?.Invoke(vm);
        Current = vm;
        _navigating = true;
        SelectedNav = Nav.FirstOrDefault(n => n.Page == page);
        _navigating = false;
        _ = vm.LoadAsync();
    }

    [RelayCommand]
    private void Search()
    {
        var q = SearchText.Trim();
        if (q.Length == 0) return;
        GoTo<SearchViewModel>(vm => vm.Query = q);
    }
}
