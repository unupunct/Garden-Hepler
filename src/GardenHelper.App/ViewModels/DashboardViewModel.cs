using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Library;
using GardenHelper.Core.Services;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record SowItem(string PlantId, string PlantName, string Activity, string Window);

public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly KnowledgeBase _kb;
    private readonly LibraryService _library;
    private readonly INavigator _nav;
    private readonly AlertService _alerts;

    public DashboardViewModel(Repository repo, KnowledgeBase kb, LibraryService library, INavigator nav, AlertService alerts, ILogger<DashboardViewModel> log) : base(log)
    {
        _repo = repo; _kb = kb; _library = library; _nav = nav; _alerts = alerts;
    }

    public ObservableCollection<AlertRow> NextActions { get; } = new();
    [RelayCommand] private void OpenMonitor() => _nav.GoTo<MonitorViewModel>();
    [RelayCommand] private void OpenDesigner() => _nav.GoTo<DesignerViewModel>();

    public ObservableCollection<SowItem> SowNow { get; } = new();
    public ObservableCollection<GardenTask> Overdue { get; } = new();
    public ObservableCollection<GardenTask> Today { get; } = new();
    public ObservableCollection<string> SeedWarnings { get; } = new();
    public ObservableCollection<JournalEntry> RecentJournal { get; } = new();

    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private string _seasonText = "";
    [ObservableProperty] private string _gardenName = "";
    [ObservableProperty] private string _heading = "";
    [ObservableProperty] private bool _hasGarden;
    [ObservableProperty] private bool _hasFrostDate;
    [ObservableProperty] private string _libraryText = "";
    [ObservableProperty] private Book? _lastBook;
    [ObservableProperty] private bool _isNewUser;
    [ObservableProperty] private bool _hasBooks;
    [ObservableProperty] private bool _hasSeeds;
    [ObservableProperty] private bool _hasTasks;

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var garden = await _repo.ActiveGardenAsync();
        HasGarden = garden is not null;
        HasFrostDate = garden?.LastSpringFrost is not null;
        GardenName = garden?.Name ?? "";
        Heading = string.IsNullOrWhiteSpace(GardenName) ? Loc.T("dash_title") : GardenName;
        DateText = today.ToString("dddd, d MMMM yyyy", Loc.Instance.Culture);
        SeasonText = Loc.Enum("season", PlantingCalculator.Season(today));

        SowNow.Clear();
        if (garden is not null)
            foreach (var p in _kb.Plants.OrderBy(p => p.DisplayName(Loc.Instance.Language)))
                foreach (var w in PlantingCalculator.Calculate(p, garden, today.Year).Windows.Where(w => w.Contains(today)))
                    SowNow.Add(new SowItem(p.Id, p.DisplayName(Loc.Instance.Language), Loc.T("act_" + w.Activity),
                        $"{w.Start.ToString("d MMM", Loc.Instance.Culture)} – {w.End.ToString("d MMM", Loc.Instance.Culture)}"));

        var open = await _repo.ListAsync<GardenTask>(q => q.Where(t => !t.Done).OrderBy(t => t.DueDate));
        Fill(Overdue, open.Where(t => t.DueDate < today));
        Fill(Today, open.Where(t => t.DueDate == today));
        HasTasks = open.Count > 0;

        var seeds = await _repo.ListAsync<SeedItem>();
        HasSeeds = seeds.Count > 0;
        SeedWarnings.Clear();
        foreach (var s in seeds)
        {
            if (s.ReviewDate is { } d && d <= today) SeedWarnings.Add(Loc.F("seed_review_due", s.Name));
            else if (s.Quantity > 0 && s.QuantityRemaining <= s.Quantity * 0.2m) SeedWarnings.Add(Loc.F("seed_running_low", s.Name));
        }

        Fill(RecentJournal, (await _repo.ListAsync<JournalEntry>(q => q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).Take(4))));

        Fill(NextActions, (await _alerts.LoadAsync()).Take(5));

        var stats = await _library.StatsAsync();
        HasBooks = stats.Books > 0;
        LibraryText = Loc.F("dash_library_stats", stats.Books, stats.Words.ToString("N0", Loc.Instance.Culture));
        var books = await _library.ListAsync();
        LastBook = books.Where(b => b.LastReadUtc != null).OrderByDescending(b => b.LastReadUtc).FirstOrDefault();
        IsNewUser = !HasGarden && !HasBooks && !HasSeeds && !HasTasks;
    });

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }

    [RelayCommand] private void OpenPlant(SowItem item) => _nav.GoTo<PlantsViewModel>(vm => vm.PendingSelect = item.PlantId);
    [RelayCommand] private void OpenGarden() => _nav.GoTo<GardenViewModel>();
    [RelayCommand] private void OpenLibrary() => _nav.GoTo<LibraryViewModel>();
    [RelayCommand] private void OpenTasks() => _nav.GoTo<TasksViewModel>();
    [RelayCommand] private void OpenSeeds() => _nav.GoTo<SeedsViewModel>();
    [RelayCommand] private void OpenPlants() => _nav.GoTo<PlantsViewModel>();
    [RelayCommand] private void OpenJournal() => _nav.GoTo<JournalViewModel>();

    [RelayCommand]
    private void ContinueReading()
    {
        if (LastBook is { } b) _nav.GoTo<ReaderViewModel>(vm => vm.Open(b.Id, Math.Max(1, b.LastReadSection), null));
    }

    [RelayCommand]
    private Task CompleteAsync(GardenTask task) => RunAsync(async () =>
    {
        task.Done = true; task.CompletedUtc = DateTime.UtcNow;
        await _repo.SaveAsync(task);
        await LoadAsync();
    }, busy: false);
}
