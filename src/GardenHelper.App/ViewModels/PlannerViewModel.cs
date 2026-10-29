using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Planning;
using GardenHelper.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record PlannerBar(double Left, double Width, double Top, Brush Fill, string Tip);
public sealed record PlannerRow(string Name, IReadOnlyList<PlannerBar> Bars);

/// <summary>The planting calendar: for your crops (seeds, this year's plantings, and any you add), when to sow indoors, sow outside, plant out and harvest.</summary>
public sealed partial class PlannerViewModel : PageViewModel
{
    public const double ChartWidth = 840;
    private readonly Repository _repo;
    private readonly KnowledgeBase _kb;
    private readonly HashSet<string> _extra = new();

    public PlannerViewModel(Repository repo, KnowledgeBase kb, ILogger<PlannerViewModel> log) : base(log)
    {
        _repo = repo; _kb = kb;
        var y = DateTime.Today.Year; Years = new[] { y, y + 1 };
        _year = y;
        foreach (var p in _kb.Plantable.OrderBy(p => p.DisplayName(Loc.Instance.Language))) AllCrops.Add(new PlantOption(p.Id, p.DisplayName(Loc.Instance.Language)));
        Months = Enumerable.Range(1, 12).Select(m => new DateTime(2001, m, 1).ToString("MMM", Loc.Instance.Culture)).ToList();
    }

    public IReadOnlyList<int> Years { get; }
    public IReadOnlyList<string> Months { get; }
    public ObservableCollection<PlantOption> AllCrops { get; } = new();
    public ObservableCollection<PlannerRow> Rows { get; } = new();
    [ObservableProperty] private int _year;
    [ObservableProperty] private PlantOption? _newCrop;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private double _todayLeft = -10;
    [ObservableProperty] private bool _showToday;

    partial void OnYearChanged(int value) => _ = LoadAsync();

    private static Brush Frozen(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    private static readonly Dictionary<string, (string Color, double Top)> Style = new()
    {
        ["sow_indoors"] = ("#F2C94C", 3), ["sow_direct"] = ("#8CC152", 3), ["transplant"] = ("#4FA3C7", 19), ["plant"] = ("#4FA3C7", 19), ["harvest"] = ("#E9875B", 35),
    };

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var garden = await _repo.ActiveGardenAsync();
        var seeds = await _repo.ListAsync<SeedItem>(null);
        var beds = await _repo.ListAsync<GardenBed>(q => q.Include(b => b.Plantings));
        var ids = new HashSet<string>(_extra);
        foreach (var s in seeds.Where(s => s.PlantId is not null)) ids.Add(s.PlantId!);
        foreach (var p in beds.SelectMany(b => b.Plantings).Where(p => p.PlantedDate?.Year == Year || p.Status is PlantingStatus.Planned or PlantingStatus.Sown)) ids.Add(p.PlantId);
        var plants = ids.Select(i => _kb.Get(i)).Where(p => p is not null).Cast<PlantRecord>().OrderBy(p => p.DisplayName(Loc.Instance.Language)).ToList();

        var culture = Loc.Instance.Culture; var lang = Loc.Instance.Language;
        var days = DateTime.IsLeapYear(Year) ? 366.0 : 365.0;
        Rows.Clear();
        foreach (var row in PlantingCalendar.Build(plants, garden, Year))
        {
            var name = _kb.Get(row.PlantId)?.DisplayName(lang) ?? row.PlantId;
            var bars = row.Bars.Select(b =>
            {
                var (color, top) = Style[b.Kind];
                var start = new DateOnly(Year, 1, 1).AddDays(b.StartDay - 1); var end = new DateOnly(Year, 1, 1).AddDays(b.EndDay - 1);
                return new PlannerBar(ChartWidth * (b.StartDay - 1) / days, Math.Max(6, ChartWidth * (b.EndDay - b.StartDay + 1) / days), top, Frozen(color),
                    $"{Loc.T("plan_k_" + b.Kind)}: {start.ToString("d MMM", culture)} – {end.ToString("d MMM", culture)}");
            }).ToList();
            Rows.Add(new PlannerRow(name, bars));
        }
        IsEmpty = Rows.Count == 0;
        ShowToday = Year == DateTime.Today.Year; TodayLeft = ChartWidth * (DateTime.Today.DayOfYear - 1) / days;
    });

    [RelayCommand]
    private Task AddCropAsync() => RunAsync(async () =>
    {
        if (NewCrop?.Id is { } id) { _extra.Add(id); NewCrop = null; await LoadAsync(); }
    }, busy: false);
}
