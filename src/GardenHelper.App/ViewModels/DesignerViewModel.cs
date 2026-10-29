using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Data;
using GardenHelper.Core.Design;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

/// <summary>A bed (or other object) on the plan. Positions are centimetres; the view scales them.</summary>
public sealed partial class BedItem : ObservableObject
{
    public BedItem(GardenBed bed) { Bed = bed; }
    public GardenBed Bed { get; }

    public int Id => Bed.Id;
    public double X { get => Bed.X; set { Bed.X = value; OnPropertyChanged(); } }
    public double Y { get => Bed.Y; set { Bed.Y = value; OnPropertyChanged(); } }
    public double Width { get => Bed.Width; set { Bed.Width = Math.Max(20, value); OnPropertyChanged(); OnPropertyChanged(nameof(SizeText)); OnPropertyChanged(nameof(AreaText)); } }
    public double Height { get => Bed.Height; set { Bed.Height = Math.Max(20, value); OnPropertyChanged(); OnPropertyChanged(nameof(SizeText)); OnPropertyChanged(nameof(AreaText)); } }
    public double Rotation { get => Bed.Rotation; set { Bed.Rotation = ((value % 360) + 360) % 360; OnPropertyChanged(); } }
    public string Name { get => Bed.Name; set { Bed.Name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Label)); } }
    public int ShadePercent { get => Bed.ShadePercent; set { Bed.ShadePercent = Math.Clamp(value, 0, 90); OnPropertyChanged(); } }
    public string Kind { get => Bed.Kind; set { Bed.Kind = value; OnPropertyChanged(); OnPropertyChanged(nameof(Fill)); } }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isHighlighted;
    [ObservableProperty] private string _plantsText = "";

    public string Label => Name;
    public string SizeText => $"{Width / 100:0.##} × {Height / 100:0.##} m";
    public string AreaText => $"{Width * Height / 10000:0.##} m²";
    public Pt[] Corners => GeometryMath.RectCorners(X, Y, Width, Height, Rotation);

    public Brush Fill => new SolidColorBrush(Kind switch
    {
        "raised" => Color.FromArgb(190, 150, 110, 70), "greenhouse" => Color.FromArgb(170, 150, 210, 220),
        "pond" => Color.FromArgb(190, 70, 130, 190), "compost" => Color.FromArgb(190, 110, 80, 50),
        "tree" => Color.FromArgb(190, 60, 120, 60), "path" => Color.FromArgb(150, 200, 190, 160),
        "structure" => Color.FromArgb(190, 150, 150, 150), _ => Color.FromArgb(185, 140, 175, 100),
    });
}

public sealed record KindOption(string Key, string Label)
{
    public override string ToString() => Label;
}
public sealed record PlantingRow(BedPlanting Planting, string PlantName, string StatusLabel, string Expected)
{
    public string SownArg => $"{Planting.Id}:Sown";
    public string GrowingArg => $"{Planting.Id}:Growing";
    public string HarvestingArg => $"{Planting.Id}:Harvesting";
    public string FinishedArg => $"{Planting.Id}:Finished";
    public string Quantity => "×" + Planting.Quantity;
}

public sealed record LibraryFilter(string Key, string Label, Func<PlantRecord, bool> Match);
public sealed record PlantCard(PlantRecord Plant, string Name, string Scientific, string Category, string Sun, string Spacing, string Days, string Window)
{
    public override string ToString() => $"{Name} ({Scientific})";
}
public sealed record MonthRow(int Month, string Name, int Count)
{
    public string Text => Count > 0 ? $"{Name}  ·  {Count}" : Name;
    public override string ToString() => Text;
}
public sealed record ActivityRow(MonthActivity Activity, string Text, string Dates);
public sealed record BedState(double X, double Y, double W, double H, double Rot, string Name, int Shade, string Kind);

public sealed partial class DesignerViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly KnowledgeBase _kb;
    private readonly AppPaths _paths;
    private readonly AlertService _alerts;
    private readonly IDialogs _dialogs;
    private readonly INavigator _nav;
    private Garden? _garden;
    private readonly Dictionary<int, BedState> _snapshots = new();
    private List<MonthActivity> _yearActivities = new();
    private List<PlantCard> _allCards = new();

    public UndoStack History { get; } = new();

    public DesignerViewModel(Repository repo, KnowledgeBase kb, AppPaths paths, AlertService alerts, IDialogs dialogs, INavigator nav, ILogger<DesignerViewModel> log) : base(log)
    {
        _repo = repo; _kb = kb; _paths = paths; _alerts = alerts; _dialogs = dialogs; _nav = nav;
        var lang = Loc.Instance.Language;
        Plants = kb.Plants.OrderBy(p => p.DisplayName(lang)).Select(p => new PlantOption(p.Id, p.DisplayName(lang))).ToList();
        Kinds = new[] { "bed", "raised", "greenhouse", "tree", "pond", "compost", "path", "structure" }.Select(k => new KindOption(k, Loc.T("kind_" + k))).ToList();
        Filters = new[]
        {
            new LibraryFilter("all", Loc.T("filter_all"), _ => true),
            new LibraryFilter("edible", Loc.T("filter_edible"), p => p.Roles.Contains("food")),
            new LibraryFilter("herbs", Loc.T("filter_herbs"), p => p.Category == "herb"),
            new LibraryFilter("flowers", Loc.T("filter_flowers"), p => p.Category == "flower" || p.Roles.Contains("edible-flower")),
            new LibraryFilter("perennial", Loc.T("filter_perennial"), p => p.Lifecycle?.StartsWith("perennial") == true),
            new LibraryFilter("trees", Loc.T("filter_trees"), p => p.Category is "fruit-tree" or "nut-tree" or "berry"),
            new LibraryFilter("cover", Loc.T("filter_cover"), p => p.Category == "cover-crop"),
            new LibraryFilter("nfix", Loc.T("filter_nfix"), p => p.NitrogenFixing),
        };
        SelectedFilter = Filters[0];
        NewPlantedDate = DateTime.Today;
        History.Changed += () => { OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo)); OnPropertyChanged(nameof(UndoTip)); OnPropertyChanged(nameof(RedoTip)); };
    }

    public IReadOnlyList<PlantOption> Plants { get; }
    public IReadOnlyList<KindOption> Kinds { get; }
    public IReadOnlyList<LibraryFilter> Filters { get; }
    public ObservableCollection<BedItem> Beds { get; } = new();
    public ObservableCollection<PlantingRow> Plantings { get; } = new();
    public ObservableCollection<AlertRow> BedAlerts { get; } = new();
    public ObservableCollection<PlantCard> Cards { get; } = new();
    public ObservableCollection<MonthRow> Months { get; } = new();
    public ObservableCollection<ActivityRow> MonthActivities { get; } = new();

    [ObservableProperty] private bool _hasGarden;
    [ObservableProperty] private BedItem? _selected;
    [ObservableProperty] private double _scale = 0.5;
    [ObservableProperty] private double _canvasWidth = 1600;
    [ObservableProperty] private double _canvasHeight = 1000;
    [ObservableProperty] private System.Windows.Media.ImageSource? _satellite;
    [ObservableProperty] private bool _snap = true;
    [ObservableProperty] private PlantOption? _newPlant;
    [ObservableProperty] private string _newQuantity = "1";
    [ObservableProperty] private DateTime? _newPlantedDate;
    [ObservableProperty] private KindOption? _selectedKind;
    [ObservableProperty] private string _librarySearch = "";
    [ObservableProperty] private LibraryFilter? _selectedFilter;
    [ObservableProperty] private MonthRow? _selectedMonth;
    [ObservableProperty] private string _cursorText = "";
    [ObservableProperty] private string _overlapText = "";
    [ObservableProperty] private int _panelTab;          // 0 = plant library, 1 = selected object
    [ObservableProperty] private string _rotationText = "";
    [ObservableProperty] private string _rotationGlyph = "";
    [ObservableProperty] private string _rotationWhy = "";
    [ObservableProperty] private bool _hasRotation;
    [ObservableProperty] private string _fitText = "";
    [ObservableProperty] private bool _leftOpen = true;

    public double LabelSize => 13 / Scale;
    public double HandleSize => 16 / Scale;
    public bool HasSelection => Selected is not null;
    public string ScaleText => $"{Scale * 100:0}%";
    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;
    public string UndoTip => History.UndoText is { } t ? Loc.F("design_undo_tip", t) : Loc.T("design_undo");
    public string RedoTip => History.RedoText is { } t ? Loc.F("design_redo_tip", t) : Loc.T("design_redo");
    public string StatusSelection => Selected is null ? Loc.T("design_status_none")
        : Loc.F("design_status_sel", Selected.Name, Selected.SizeText, Selected.AreaText, Selected.Rotation.ToString("0"));

    partial void OnScaleChanged(double value) { OnPropertyChanged(nameof(LabelSize)); OnPropertyChanged(nameof(HandleSize)); OnPropertyChanged(nameof(ScaleText)); }
    partial void OnLibrarySearchChanged(string value) => RefreshCards();
    partial void OnSelectedFilterChanged(LibraryFilter? value) => RefreshCards();
    partial void OnNewPlantChanged(PlantOption? value) => UpdatePlantAdvice();
    partial void OnNewPlantedDateChanged(DateTime? value) => UpdatePlantAdvice();
    partial void OnSelectedMonthChanged(MonthRow? value) => ShowMonth();

    partial void OnSelectedChanged(BedItem? oldValue, BedItem? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) { newValue.IsSelected = true; SelectedKind = Kinds.First(k => k.Key == newValue.Kind); PanelTab = 1; }
        OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(StatusSelection));
        UpdateOverlap();
        _ = RefreshPlantingsAsync();
    }

    partial void OnSelectedKindChanged(KindOption? value)
    {
        if (Selected is not null && value is not null && Selected.Kind != value.Key) Selected.Kind = value.Key;
    }

    public override Task LoadAsync() => RunAsync(async () =>
    {
        _garden = await _repo.ActiveGardenAsync();
        HasGarden = _garden is not null;
        if (_garden is null) return;

        if (SiteImage.Load(_paths, _garden) is { } img && _garden.SatelliteCmPerPixel is { } cm)
        {
            Satellite = img;
            var bmp = (System.Windows.Media.Imaging.BitmapSource)img;
            CanvasWidth = bmp.PixelWidth * cm; CanvasHeight = bmp.PixelHeight * cm;
            Scale = 0.08;
        }
        else { Satellite = null; CanvasWidth = 1600; CanvasHeight = 1000; }

        var keep = Selected?.Id;
        var beds = await _repo.ListAsync<GardenBed>(q => q.Where(b => b.GardenId == _garden.Id).Include(b => b.Plantings).OrderBy(b => b.Id));
        Beds.Clear(); _snapshots.Clear();
        foreach (var b in beds) { Beds.Add(Wrap(b)); _snapshots[b.Id] = Capture(b); }
        Selected = Beds.FirstOrDefault(b => b.Id == keep);
        BuildCards();
        RebuildCalendar();
        await RefreshAlertsAsync();
    });

    private BedItem Wrap(GardenBed bed) => new(bed) { PlantsText = PlantsSummary(bed.Plantings) };

    private string PlantsSummary(IEnumerable<BedPlanting> plantings) =>
        string.Join(", ", plantings.Where(p => p.Status != PlantingStatus.Finished)
            .Select(p => $"{_kb.Get(p.PlantId)?.DisplayName(Loc.Instance.Language) ?? p.PlantId} ×{p.Quantity}"));

    // ---------------- Plant library ----------------
    private void BuildCards()
    {
        var lang = Loc.Instance.Language;
        _allCards = _kb.Plants.OrderBy(p => p.DisplayName(lang)).Select(p =>
        {
            var first = PlantingCalculator.Calculate(p, _garden, DateTime.Today.Year).Windows.FirstOrDefault();
            var window = first is null ? "" : $"{Loc.T("act_" + first.Activity)}: {first.Start.ToString("d MMM", Loc.Instance.Culture)} – {first.End.ToString("d MMM", Loc.Instance.Culture)}";
            return new PlantCard(p, p.DisplayName(lang), p.Scientific, Loc.Enum("cat", p.Category), Loc.Enum("sun", p.Sun),
                p.SpacingCm is null ? "" : UnitFormatter.Range(p.SpacingCm), p.DaysToMaturity is { } d ? $"{d.Min}–{d.Max} d" : "", window);
        }).ToList();
        RefreshCards();
    }

    private void RefreshCards()
    {
        var f = Core.Library.SubjectExtractor.Fold(LibrarySearch ?? "");
        var match = SelectedFilter?.Match ?? (_ => true);
        Cards.Clear();
        foreach (var c in _allCards.Where(c => match(c.Plant) && (f.Length == 0 ||
                     Core.Library.SubjectExtractor.Fold(string.Join(" ", c.Plant.Names.Values.SelectMany(x => x)) + " " + c.Plant.Scientific).Contains(f))))
            Cards.Add(c);
    }

    [RelayCommand]
    private Task AddCardToSelectedAsync(PlantCard card) => AddPlantToBedAsync(Selected, card.Plant.Id);

    /// <summary>Adds a plant to a bed. Quantity comes from the spacing-based fit; a rotation conflict is allowed but recorded.</summary>
    public Task AddPlantToBedAsync(BedItem? bed, string plantId) => RunAsync(async () =>
    {
        if (bed is null) { StatusMessage = Loc.T("design_select_bed_first"); return; }
        var plant = _kb.Get(plantId); if (plant is null) return;
        var grid = plant.SpacingCm is { } s ? PlantLayout.Fit(bed.Width, bed.Height, s.Max, plant.RowSpacingCm?.Max) : null;
        var qty = Math.Max(1, grid?.Total ?? 1);
        var date = Dates.ToDateOnly(NewPlantedDate) ?? DateOnly.FromDateTime(DateTime.Today);
        var check = RotationAdvisor.Check(plant, date.Year, bed.Bed.Plantings, _kb, Loc.Instance.Language);
        var note = check.Verdict == RotationVerdict.Conflict ? Loc.T("rot_override_note") : null;
        await _repo.SaveAsync(new BedPlanting { BedId = bed.Id, PlantId = plantId, Quantity = qty, Notes = note });
        if (!ReferenceEquals(bed, Selected)) Selected = bed;
        await RefreshPlantingsAsync();
        StatusMessage = Loc.F("design_added_plant", plant.DisplayName(Loc.Instance.Language), qty, bed.Name);
    }, busy: false);

    [RelayCommand] private void OpenProfile(PlantCard card) => _nav.GoTo<PlantsViewModel>(vm => vm.PendingSelect = card.Plant.Id);

    private void UpdatePlantAdvice()
    {
        HasRotation = false; FitText = "";
        if (Selected is null || NewPlant?.Id is null || _kb.Get(NewPlant.Id) is not { } plant) return;
        var year = NewPlantedDate?.Year ?? DateTime.Today.Year;
        var c = RotationAdvisor.Check(plant, year, Selected.Bed.Plantings, _kb, Loc.Instance.Language);
        RotationText = Loc.F(c.Key, c.Args.Cast<object>().ToArray());
        RotationGlyph = c.Verdict switch { RotationVerdict.Compatible => "✔", RotationVerdict.Caution => "◔", RotationVerdict.Conflict => "⚠", RotationVerdict.Insufficient => "?", _ => "–" };
        RotationWhy = Loc.T("rotv_" + c.Verdict) + (c.RuleReason is { Length: > 0 } r ? " — " + r : "");
        HasRotation = true;
        if (plant.SpacingCm is { } sp)
        {
            var g = PlantLayout.Fit(Selected.Width, Selected.Height, sp.Max, plant.RowSpacingCm?.Max);
            FitText = Loc.F("design_fit_text", g.Total, g.Columns, g.Rows, UnitFormatter.Length(sp.Max));
        }
    }

    // ---------------- Month planner ----------------
    private void RebuildCalendar()
    {
        var year = DateTime.Today.Year;
        _yearActivities = CalendarPlanner.ForYear(_garden, Beds.Select(b => b.Bed), _kb, year, Loc.Instance.Language);
        var keep = SelectedMonth?.Month;
        Months.Clear();
        for (var m = 1; m <= 12; m++)
            Months.Add(new MonthRow(m, new DateTime(year, m, 1).ToString("MMMM", Loc.Instance.Culture), _yearActivities.Count(a => a.Month == m)));
        SelectedMonth = Months.FirstOrDefault(r => r.Month == (keep ?? DateTime.Today.Month));
    }

    private void ShowMonth()
    {
        MonthActivities.Clear();
        foreach (var b in Beds) b.IsHighlighted = false;
        if (SelectedMonth is null) return;
        foreach (var a in CalendarPlanner.ForMonth(_yearActivities, SelectedMonth.Month))
        {
            MonthActivities.Add(new ActivityRow(a, $"{Loc.T("act_" + a.Kind)}: {a.PlantName} · {a.BedName}",
                $"{a.Start.ToString("d MMM", Loc.Instance.Culture)} – {a.End.ToString("d MMM", Loc.Instance.Culture)}"));
            if (Beds.FirstOrDefault(b => b.Id == a.BedId) is { } bed) bed.IsHighlighted = true;
        }
    }

    [RelayCommand] private void OpenActivity(ActivityRow row) { if (Beds.FirstOrDefault(b => b.Id == row.Activity.BedId) is { } b) Selected = b; }
    [RelayCommand] private void SelectBed(BedItem bed) => Selected = bed;
    [RelayCommand] private void ToggleLeft() => LeftOpen = !LeftOpen;

    // ---------------- Alerts, overlap ----------------
    private async Task RefreshAlertsAsync()
    {
        var all = await _alerts.LoadAsync();
        var ids = Selected?.Bed.Plantings.Select(p => (int?)p.Id).ToHashSet() ?? new();
        BedAlerts.Clear();
        foreach (var a in all.Where(a => a.Alert.PlantingId is { } id && ids.Contains(id) || a.Alert.Args.Contains(Selected?.Name ?? "\0")))
            BedAlerts.Add(a);
    }

    public void UpdateOverlap()
    {
        OnPropertyChanged(nameof(StatusSelection));
        if (Selected is null || Selected.Kind == "path") { OverlapText = ""; return; }
        var mine = Selected.Corners;
        var hit = Beds.Where(o => o != Selected && o.Kind != "path" && GeometryMath.ConvexOverlap(mine, o.Corners)).Select(o => o.Name).ToList();
        OverlapText = hit.Count == 0 ? "" : Loc.F("design_overlap", string.Join(", ", hit));
    }

    private async Task RefreshPlantingsAsync()
    {
        Plantings.Clear(); BedAlerts.Clear();
        if (Selected is null) return;
        var list = await _repo.ListAsync<BedPlanting>(q => q.Where(p => p.BedId == Selected.Id).OrderBy(p => p.Status).ThenBy(p => p.Id));
        Selected.Bed.Plantings = list;
        foreach (var p in list) Plantings.Add(Row(p));
        Selected.PlantsText = PlantsSummary(list);
        UpdatePlantAdvice();
        RebuildCalendar();
        await RefreshAlertsAsync();
    }

    private PlantingRow Row(BedPlanting p)
    {
        var plant = _kb.Get(p.PlantId);
        var expected = p.PlantedDate is { } d && plant?.DaysToMaturity is { } m
            ? Loc.F("design_expected", d.AddDays(m.Min).ToString("d MMM", Loc.Instance.Culture), d.AddDays(m.Max).ToString("d MMM", Loc.Instance.Culture)) : "";
        return new PlantingRow(p, plant?.DisplayName(Loc.Instance.Language) ?? p.PlantId, Loc.T("status_" + p.Status), expected);
    }

    // ---------------- Editing with undo ----------------
    private static BedState Capture(GardenBed b) => new(b.X, b.Y, b.Width, b.Height, b.Rotation, b.Name, b.ShadePercent, b.Kind);

    private static void Apply(BedItem item, BedState s)
    {
        item.X = s.X; item.Y = s.Y; item.Width = s.W; item.Height = s.H; item.Rotation = s.Rot;
        item.Name = s.Name; item.ShadePercent = s.Shade; item.Kind = s.Kind;
    }

    /// <summary>Saves the bed and, if anything changed since the last save, records an undo step.</summary>
    public async Task CommitAsync(BedItem item, string description)
    {
        var after = Capture(item.Bed);
        var before = _snapshots.GetValueOrDefault(item.Id);
        await PersistAsync(item.Bed);
        UpdateOverlap();
        if (before is null || before == after) return;
        _snapshots[item.Id] = after;
        History.Push(new UndoEntry(description,
            async () => { Apply(item, before); _snapshots[item.Id] = before; await PersistAsync(item.Bed); UpdateOverlap(); },
            async () => { Apply(item, after); _snapshots[item.Id] = after; await PersistAsync(item.Bed); UpdateOverlap(); }));
    }

    public Task PersistAsync(GardenBed bed) => RunAsync(async () =>
    {
        var plantings = bed.Plantings; bed.Plantings = new();
        await _repo.SaveAsync(bed);
        bed.Plantings = plantings;
    }, busy: false);

    public double SnapValue(double v) => Snap ? Math.Round(v / 10) * 10 : v;

    [RelayCommand]
    private Task AddObjectAsync(string kind) => RunAsync(async () =>
    {
        if (_garden is null) return;
        var (w, h) = kind switch { "tree" => (300.0, 300.0), "pond" => (400.0, 300.0), "greenhouse" => (300.0, 400.0), "path" => (80.0, 600.0), "compost" => (100.0, 100.0), "structure" => (300.0, 300.0), _ => (120.0, 300.0) };
        var offset = Beds.Count * 40 % 400;
        var bed = await _repo.SaveAsync(new GardenBed
        {
            GardenId = _garden.Id, Kind = kind, Name = $"{Loc.T("kind_" + kind)} {Beds.Count(b => b.Kind == kind) + 1}",
            X = CanvasWidth / 2 - w / 2 + offset, Y = CanvasHeight / 2 - h / 2 + offset, Width = w, Height = h,
        });
        var item = Wrap(bed);
        Beds.Add(item); _snapshots[bed.Id] = Capture(bed); Selected = item;
        History.Push(new UndoEntry(Loc.F("design_undo_add", item.Name),
            () => RemoveAsync(item), async () => item = await RecreateAsync(item)));
    }, busy: false);

    private async Task RemoveAsync(BedItem item)
    {
        var plantings = item.Bed.Plantings; item.Bed.Plantings = new();
        await _repo.DeleteAsync(item.Bed);
        item.Bed.Plantings = plantings;
        if (Selected == item) Selected = null;
        Beds.Remove(item); _snapshots.Remove(item.Id);
        RebuildCalendar();
    }

    /// <summary>Recreates a deleted bed with its plantings (new database ids).</summary>
    private async Task<BedItem> RecreateAsync(BedItem old)
    {
        var o = old.Bed;
        var copy = new GardenBed
        {
            GardenId = o.GardenId, Kind = o.Kind, Name = o.Name, X = o.X, Y = o.Y, Width = o.Width, Height = o.Height, Rotation = o.Rotation,
            ShadePercent = o.ShadePercent, Notes = o.Notes,
            Plantings = o.Plantings.Select(p => new BedPlanting { PlantId = p.PlantId, Variety = p.Variety, Quantity = p.Quantity, PlantedDate = p.PlantedDate, Status = p.Status, Notes = p.Notes }).ToList(),
        };
        await _repo.SaveAsync(copy);
        var item = Wrap(copy);
        Beds.Add(item); _snapshots[copy.Id] = Capture(copy); Selected = item;
        RebuildCalendar();
        return item;
    }

    [RelayCommand]
    private Task SaveSelectedAsync() => RunAsync(async () =>
    {
        if (Selected is null) return;
        await CommitAsync(Selected, Loc.F("design_undo_edit", Selected.Name));
        StatusMessage = Loc.T("saved");
    }, busy: false);

    [RelayCommand]
    private Task RotateAsync(string degrees) => RunAsync(async () =>
    {
        if (Selected is null) return;
        Selected.Rotation += double.Parse(degrees, System.Globalization.CultureInfo.InvariantCulture);
        await CommitAsync(Selected, Loc.F("design_undo_rotate", Selected.Name));
    }, busy: false);

    [RelayCommand]
    private Task DeleteSelectedAsync() => RunAsync(async () =>
    {
        if (Selected is null || !_dialogs.Confirm(Loc.F("design_confirm_delete", Selected.Name))) return;
        var item = Selected;
        await RemoveAsync(item);
        History.Push(new UndoEntry(Loc.F("design_undo_delete", item.Name),
            async () => item = await RecreateAsync(item), () => RemoveAsync(item)));
        await RefreshAlertsAsync();
    }, busy: false);

    [RelayCommand] private Task UndoAsync() => RunAsync(async () => { await History.UndoAsync(); await RefreshPlantingsAsync(); }, busy: false);
    [RelayCommand] private Task RedoAsync() => RunAsync(async () => { await History.RedoAsync(); await RefreshPlantingsAsync(); }, busy: false);

    [RelayCommand] private void ZoomIn() => Scale = Math.Min(3, Math.Round(Scale * 1.25, 3));
    [RelayCommand] private void ZoomOut() => Scale = Math.Max(0.03, Math.Round(Scale / 1.25, 3));
    [RelayCommand] private void Fit() => Scale = Math.Round(Math.Min(780 / CanvasWidth, 600 / CanvasHeight), 3);
    [RelayCommand] private void OpenGarden() => _nav.GoTo<GardenViewModel>();
    [RelayCommand] private void OpenSite() => _nav.GoTo<SiteViewModel>();

    // ---------------- Plantings ----------------
    [RelayCommand]
    private Task AddPlantingAsync() => RunAsync(async () =>
    {
        if (Selected is null || NewPlant?.Id is null) return;
        var qty = int.TryParse(NewQuantity, out var q) && q > 0 ? q : 1;
        var plant = _kb.Get(NewPlant.Id)!;
        var date = Dates.ToDateOnly(NewPlantedDate);
        var check = RotationAdvisor.Check(plant, date?.Year ?? DateTime.Today.Year, Selected.Bed.Plantings, _kb, Loc.Instance.Language);
        await _repo.SaveAsync(new BedPlanting
        {
            BedId = Selected.Id, PlantId = NewPlant.Id, Quantity = qty, PlantedDate = date,
            Notes = check.Verdict == RotationVerdict.Conflict ? Loc.T("rot_override_note") : null,
        });
        await RefreshPlantingsAsync();
    }, busy: false);

    [RelayCommand]
    private void UseFitQuantity()
    {
        if (Selected is null || NewPlant?.Id is null || _kb.Get(NewPlant.Id)?.SpacingCm is not { } sp) return;
        NewQuantity = PlantLayout.Fit(Selected.Width, Selected.Height, sp.Max, _kb.Get(NewPlant.Id)!.RowSpacingCm?.Max).Total.ToString();
    }

    [RelayCommand]
    private Task SetStatusAsync(string arg) => RunAsync(async () =>
    {
        var parts = arg.Split(':');
        var row = Plantings.FirstOrDefault(r => r.Planting.Id == int.Parse(parts[0]));
        if (row is null) return;
        row.Planting.Status = Enum.Parse<PlantingStatus>(parts[1]);
        if (row.Planting.Status == PlantingStatus.Sown && row.Planting.PlantedDate is null) row.Planting.PlantedDate = DateOnly.FromDateTime(DateTime.Today);
        var bed = row.Planting.Bed; row.Planting.Bed = null;
        await _repo.SaveAsync(row.Planting);
        row.Planting.Bed = bed;
        await RefreshPlantingsAsync();
    }, busy: false);

    [RelayCommand]
    private Task RemovePlantingAsync(PlantingRow row) => RunAsync(async () =>
    {
        row.Planting.Bed = null;
        await _repo.DeleteAsync(row.Planting);
        await RefreshPlantingsAsync();
    }, busy: false);
}
