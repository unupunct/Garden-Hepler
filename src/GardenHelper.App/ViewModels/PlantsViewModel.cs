using GardenHelper.Core.Library;
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

public sealed record BookHitRow(int BookId, int Ordinal, string Book, string Section, string Snippet, string Term);

public sealed partial class PlantsViewModel : PageViewModel
{
    private readonly KnowledgeBase _kb;
    private readonly Repository _repo;
    private readonly INavigator _nav;
    private readonly LibraryService _library;
    private readonly ILogger _logger;
    private Garden? _garden;

    public PlantsViewModel(KnowledgeBase kb, Repository repo, INavigator nav, LibraryService library, ILogger<PlantsViewModel> log) : base(log)
    {
        _kb = kb; _repo = repo; _nav = nav; _library = library; _logger = log;
        Categories = new[] { new CategoryChip(null, Loc.T("all")) }
            .Concat(kb.Categories.Select(c => new CategoryChip(c, Loc.Enum("cat", c)))).ToList();
        SelectedCategory = Categories[0];
        Purposes = new[] { new CategoryChip(null, Loc.T("all")) }.Concat(PlantPurpose.Keys.Select(k => new CategoryChip(k, Loc.T("purpose_" + k)))).ToList();
        SelectedPurpose = Purposes[0];
    }

    public IReadOnlyList<CategoryChip> Categories { get; }
    public IReadOnlyList<CategoryChip> Purposes { get; }
    public ObservableCollection<PlantItem> Results { get; } = new();
    public ObservableCollection<DetailLine> Growing { get; } = new();
    public ObservableCollection<WindowRow> Windows { get; } = new();
    public ObservableCollection<CompanionRow> Companions { get; } = new();
    public ObservableCollection<string> Tips { get; } = new();
    public ObservableCollection<string> Roles { get; } = new();
    public ObservableCollection<BookHitRow> BookHits { get; } = new();
    [ObservableProperty] private bool _noBookHits;

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private CategoryChip? _selectedCategory;
    [ObservableProperty] private CategoryChip? _selectedPurpose;
    [ObservableProperty] private string? _benefits;
    [ObservableProperty] private bool _hasMedicinal;
    [ObservableProperty] private string? _medParts;
    [ObservableProperty] private string? _medUses;
    [ObservableProperty] private string? _medSafety;
    [ObservableProperty] private string? _edibilityText;
    [ObservableProperty] private bool _edibilityDanger;
    [ObservableProperty] private string? _protectedText;
    [ObservableProperty] private string? _originText;
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
    partial void OnSelectedPurposeChanged(CategoryChip? value) => Refresh();
    partial void OnSelectedChanged(PlantItem? value) => ShowDetail();
    partial void OnShiftDaysChanged(int value) => ShowCalendar();

    public override Task LoadAsync() => RunAsync(async () =>
    {
        _garden = await _repo.ActiveGardenAsync();
        Refresh();
        if (PendingSelect is { } id)
        {
            Query = ""; SelectedCategory = Categories[0]; SelectedPurpose = Purposes[0];
            Selected = Results.FirstOrDefault(r => r.Plant.Id == id);
            PendingSelect = null;
        }
    }, busy: false);

    private void Refresh()
    {
        var lang = Loc.Instance.Language;
        var keep = Selected?.Plant.Id;
        Results.Clear();
        foreach (var m in _kb.Search(Query, SelectedCategory?.Key, SelectedPurpose?.Key))
            Results.Add(new PlantItem(m.Plant, m.Plant.DisplayName(lang), m.Plant.Scientific, Loc.Enum("cat", m.Plant.Category)));
        if (keep is not null && Results.FirstOrDefault(r => r.Plant.Id == keep) is { } same) Selected = same;
    }

    private void ShowDetail()
    {
        Growing.Clear(); Tips.Clear(); Roles.Clear(); Companions.Clear(); BookHits.Clear(); NoBookHits = false;
        if (Selected is null) { Windows.Clear(); return; }
        var p = Selected.Plant; var lang = Loc.Instance.Language;
        _ = LoadBookHitsAsync(p);
        Summary = p.SummaryText(lang);
        Caution = p.CautionText(lang);
        ConfidenceText = Loc.F("plant_confidence", Loc.Enum("conf", p.Confidence), p.Reviewed ?? "?");
        void Line(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) Growing.Add(new DetailLine(Loc.T(key), value)); }
        Benefits = p.BenefitsText(lang);
        HasMedicinal = p.Medicinal is not null;
        MedParts = p.Medicinal?.Parts.GetValueOrDefault(lang) ?? p.Medicinal?.Parts.GetValueOrDefault("en");
        MedUses = p.Medicinal?.Uses.GetValueOrDefault(lang) ?? p.Medicinal?.Uses.GetValueOrDefault("en");
        MedSafety = p.Medicinal?.Safety.GetValueOrDefault(lang) ?? p.Medicinal?.Safety.GetValueOrDefault("en");
        EdibilityText = p.Edibility is null ? null : Loc.Enum("edib", p.Edibility);
        EdibilityDanger = p.Edibility is "toxic" or "deadly";
        ProtectedText = p.Protected ? Loc.T("plant_protected") : null;
        OriginText = p.Origin is null ? null : Loc.Enum("origin", p.Origin);
        Line("f_origin", OriginText);
        if (p.SeasonMonths is { } sm)
        {
            var months = System.Globalization.CultureInfo.CurrentUICulture.DateTimeFormat;
            var wild = p.Category is "wild-mushroom" or "mushroom";
            Line(wild ? "f_season_fruiting" : "f_season_flowering", sm.Min == sm.Max ? months.GetMonthName(sm.Min) : months.GetMonthName(sm.Min) + " – " + months.GetMonthName(sm.Max));
        }
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

    /// <summary>What the user's own books say about this plant: the best passages for its name (in the app language and in English).</summary>
    private async Task LoadBookHitsAsync(PlantRecord p)
    {
        try
        {
            var names = new[] { p.Names.GetValueOrDefault(Loc.Instance.Language)?.FirstOrDefault(), p.Names.GetValueOrDefault("en")?.FirstOrDefault() }
                .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var rows = new List<BookHitRow>();
            foreach (var name in names)
                foreach (var h in await _library.SearchAsync(name, null, 4))
                    if (!rows.Any(r => r.BookId == h.BookId && r.Ordinal == h.Ordinal))
                        rows.Add(new BookHitRow(h.BookId, h.Ordinal, h.BookTitle, h.SectionTitle, h.Snippet.Replace("[[", "").Replace("]]", "").Replace('\n', ' ').Trim(), name));
            if (Selected?.Plant.Id != p.Id) return;                      // the user moved on to another plant meanwhile
            BookHits.Clear();
            foreach (var r in rows.Take(5)) BookHits.Add(r);
            NoBookHits = BookHits.Count == 0;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Library lookup for {Plant} failed", p.Id); }
    }

    [RelayCommand]
    private void OpenBookHit(BookHitRow row) => _nav.GoTo<ReaderViewModel>(vm => vm.Open(row.BookId, row.Ordinal, row.Term));

    [RelayCommand]
    private void FindInLibrary()
    {
        if (Selected is { } s) _nav.GoTo<LibraryViewModel>(vm => vm.InitialQuery = s.Plant.Names.GetValueOrDefault(Loc.Instance.Language)?.FirstOrDefault() ?? s.Name);
    }
}
