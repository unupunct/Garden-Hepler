using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Services;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record CategoryChip(string? Key, string Label);
public sealed record PlantItem(PlantRecord Plant, string Name, string Scientific, string Category)
{
    public override string ToString() => $"{Name} ({Scientific})";
}
public sealed record WindowRow(PlantingWindow Window, string Activity, string Range);
public sealed record CompanionRow(string OtherId, string OtherName, string Effect, string Evidence, string Category, string Explanation, string? Conditions);
public sealed record DetailLine(string Label, string Value);

public sealed partial class PlantsViewModel : PageViewModel
{
    private readonly KnowledgeBase _kb;
    private readonly Repository _repo;
    private readonly INavigator _nav;
    private Garden? _garden;

    public PlantsViewModel(KnowledgeBase kb, Repository repo, INavigator nav, ILogger<PlantsViewModel> log) : base(log)
    {
        _kb = kb; _repo = repo; _nav = nav;
        Categories = new[] { new CategoryChip(null, Loc.T("all")) }
            .Concat(kb.Categories.Select(c => new CategoryChip(c, Loc.Enum("cat", c)))).ToList();
        SelectedCategory = Categories[0];
    }

    public IReadOnlyList<CategoryChip> Categories { get; }
    public ObservableCollection<PlantItem> Results { get; } = new();
    public ObservableCollection<DetailLine> Growing { get; } = new();
    public ObservableCollection<WindowRow> Windows { get; } = new();
    public ObservableCollection<CompanionRow> Companions { get; } = new();
    public ObservableCollection<string> Tips { get; } = new();
    public ObservableCollection<string> Roles { get; } = new();

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private CategoryChip? _selectedCategory;
    [ObservableProperty] private PlantItem? _selected;
    [ObservableProperty] private int _shiftDays;
    [ObservableProperty] private string? _calendarNote;
    [ObservableProperty] private bool _calendarPersonalized;
    [ObservableProperty] private string? _sowingNote;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string? _caution;
    [ObservableProperty] private string _confidenceText = "";

    public string? PendingSelect { get; set; }

    partial void OnQueryChanged(string value) => Refresh();
    partial void OnSelectedCategoryChanged(CategoryChip? value) => Refresh();
    partial void OnSelectedChanged(PlantItem? value) => ShowDetail();
    partial void OnShiftDaysChanged(int value) => ShowCalendar();

    public override Task LoadAsync() => RunAsync(async () =>
    {
        _garden = await _repo.ActiveGardenAsync();
        Refresh();
        if (PendingSelect is { } id)
        {
            Query = ""; SelectedCategory = Categories[0];
            Selected = Results.FirstOrDefault(r => r.Plant.Id == id);
            PendingSelect = null;
        }
    }, busy: false);

    private void Refresh()
    {
        var lang = Loc.Instance.Language;
        var keep = Selected?.Plant.Id;
        Results.Clear();
        foreach (var m in _kb.Search(Query, SelectedCategory?.Key))
            Results.Add(new PlantItem(m.Plant, m.Plant.DisplayName(lang), m.Plant.Scientific, Loc.Enum("cat", m.Plant.Category)));
        if (keep is not null && Results.FirstOrDefault(r => r.Plant.Id == keep) is { } same) Selected = same;
    }

    private void ShowDetail()
    {
        Growing.Clear(); Tips.Clear(); Roles.Clear(); Companions.Clear();
        if (Selected is null) { Windows.Clear(); return; }
        var p = Selected.Plant; var lang = Loc.Instance.Language;
        Summary = p.SummaryText(lang);
        Caution = p.CautionText(lang);
        ConfidenceText = Loc.F("plant_confidence", Loc.Enum("conf", p.Confidence), p.Reviewed ?? "?");
        void Line(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) Growing.Add(new DetailLine(Loc.T(key), value)); }
        Line("f_family", p.Family);
        Line("f_lifecycle", p.Lifecycle);
        Line("f_sun", Loc.Enum("sun", p.Sun));
        Line("f_water", Loc.Enum("water", p.Water));
        Line("f_frost", Loc.Enum("frost", p.FrostTolerance));
        Line("f_spacing", UnitFormatter.Range(p.SpacingCm));
        Line("f_rows", UnitFormatter.Range(p.RowSpacingCm));
        Line("f_height", UnitFormatter.Range(p.HeightCm));
        if (p.DaysToMaturity is { } d) Line("f_days", $"{d.Min}–{d.Max}");
        Line("f_rotation", p.RotationFamily);
        foreach (var t in p.TipList(lang)) Tips.Add(t);
        foreach (var r in p.Roles) Roles.Add(Loc.Enum("role", r));
        if (p.NitrogenFixing) Line("f_nfix", Loc.T("yes"));

        foreach (var r in _kb.RelationshipsFor(p.Id))
        {
            var other = _kb.Get(r.Other(p.Id));
            if (other is null) continue;
            Companions.Add(new CompanionRow(other.Id, other.DisplayName(lang), Loc.Enum("effect", r.Effect), Loc.Enum("evidence", r.Evidence),
                Loc.Enum("relcat", r.Category), r.ExplanationText(lang), r.ConditionsText(lang)));
        }
        ShiftDays = 0;
        ShowCalendar();
    }

    private void ShowCalendar()
    {
        Windows.Clear();
        if (Selected is null) return;
        var plan = PlantingCalculator.Calculate(Selected.Plant, _garden, DateTime.Today.Year, ShiftDays);
        CalendarPersonalized = plan.Personalized;
        SowingNote = Selected.Plant.Sowing?.Note?.GetValueOrDefault(Loc.Instance.Language) ?? Selected.Plant.Sowing?.Note?.GetValueOrDefault("en");
        CalendarNote = plan.Reason switch
        {
            "noData" => Loc.T("cal_no_data"),
            "noFrostDate" => Loc.T("cal_no_frost"),
            _ => Loc.T("cal_estimate"),
        };
        foreach (var w in plan.Windows)
            Windows.Add(new WindowRow(w, Loc.T("act_" + w.Activity),
                $"{w.Start.ToString("d MMM", Loc.Instance.Culture)} – {w.End.ToString("d MMM", Loc.Instance.Culture)}"));
    }

    [RelayCommand] private void Earlier() => ShiftDays -= 7;
    [RelayCommand] private void Later() => ShiftDays += 7;
    [RelayCommand] private void ResetShift() => ShiftDays = 0;
    [RelayCommand] private void OpenGarden() => _nav.GoTo<GardenViewModel>();

    [RelayCommand]
    private void OpenCompanion(CompanionRow row)
    {
        Query = ""; SelectedCategory = Categories[0];
        Selected = Results.FirstOrDefault(r => r.Plant.Id == row.OtherId);
    }

    [RelayCommand]
    private Task AddTaskAsync(WindowRow row) => RunAsync(async () =>
    {
        if (Selected is null) return;
        var key = row.Window.Activity switch { PlantingActivity.SowIndoors => "SowIndoors", PlantingActivity.SowDirect => "SowDirect", _ => "Transplant" };
        await _repo.SaveAsync(new GardenTask
        {
            Title = $"{row.Activity}: {Selected.Name}", Activity = key, PlantId = Selected.Plant.Id,
            DueDate = row.Window.Start < DateOnly.FromDateTime(DateTime.Today) ? DateOnly.FromDateTime(DateTime.Today) : row.Window.Start,
            Notes = Loc.T("cal_estimate"),
        });
        StatusMessage = Loc.T("task_added");
    }, busy: false);

    [RelayCommand]
    private void AddSeed()
    {
        if (Selected is { } s) _nav.GoTo<SeedsViewModel>(vm => vm.PendingPlantId = s.Plant.Id);
    }

    [RelayCommand]
    private void FindInLibrary()
    {
        if (Selected is { } s) _nav.GoTo<LibraryViewModel>(vm => vm.InitialQuery = s.Plant.Names.GetValueOrDefault(Loc.Instance.Language)?.FirstOrDefault() ?? s.Name);
    }
}
