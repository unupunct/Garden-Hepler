using System.Windows.Media.Imaging;
using System.IO;
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
using GardenHelper.Core.Site;
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
    public bool Locked { get => Bed.Locked; set { Bed.Locked = value; OnPropertyChanged(); OnPropertyChanged(nameof(Label)); OnPropertyChanged(nameof(LockLabel)); } }
    public string LockLabel => Locked ? Loc.T("design_unlock") : Loc.T("design_lock");
    [ObservableProperty] private IReadOnlyList<PlantMarker>? _markers;
    [ObservableProperty] private string _tipText = "";
    public int ShadePercent { get => Bed.ShadePercent; set { Bed.ShadePercent = Math.Clamp(value, 0, 90); OnPropertyChanged(); } }
    public string Kind { get => Bed.Kind; set { Bed.Kind = value; OnPropertyChanged(); OnPropertyChanged(nameof(Fill)); } }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isHighlighted;
    [ObservableProperty] private string _plantsText = "";

    public string Label => (Locked ? "🔒 " : "") + Name;
    public string SizeText => $"{Width / 100:0.##} × {Height / 100:0.##} m";
    public string AreaText => $"{Width * Height / 10000:0.##} m²";
    public Pt[] Corners => GeometryMath.RectCorners(X, Y, Width, Height, Rotation);

    public bool IsStructure => FeatureCatalog.IsStructure(Kind);

    public Brush Fill => FeatureCatalog.IsStructure(Kind)
        ? new SolidColorBrush(Color.FromArgb(205, FeatureColors.For(Kind).R, FeatureColors.For(Kind).G, FeatureColors.For(Kind).B))
        : new SolidColorBrush(ObjectColor(Kind));

    private static Color ObjectColor(string kind)
    {
        if (kind == "structure") return Color.FromArgb(190, 150, 150, 150);
        if (PlanElements.Get(kind) is { } e && ColorConverter.ConvertFromString(e.Color) is Color c) return Color.FromArgb(kind is "greenhouse" ? (byte)170 : kind is "path" or "driveway" ? (byte)150 : (byte)200, c.R, c.G, c.B);
        return Color.FromArgb(185, 140, 175, 100);
    }
}

public sealed record PaletteItem(string Key, string Label, string Glyph);
public sealed record PaletteGroup(string Title, IReadOnlyList<PaletteItem> Items);

public sealed record FeatureShape(int Id, string Name, string Kind, string KindLabel, bool IsStructure, PointCollection Points, double LabelX, double LabelY,
    Brush Fill, Brush Stroke, string AreaText, List<Pt> Plan)
{
    public bool IsSpace => !IsStructure;
    public override string ToString() => $"{Name}, {KindLabel}";
}

public sealed record KindOption(string Key, string Label)
{
    public override string ToString() => Label;
}
public sealed record PlantingRow(BedPlanting Planting, string PlantName, string StatusLabel, string Expected, Geometry? IconBody = null, Geometry? IconAccent = null, Brush? IconBodyBrush = null, Brush? IconAccentBrush = null)
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

    private readonly BackdropService _backdrops;

    public DesignerViewModel(Repository repo, KnowledgeBase kb, AppPaths paths, AlertService alerts, IDialogs dialogs, INavigator nav, BackdropService backdrops, CameraFeed camFeed, ILogger<DesignerViewModel> log) : base(log)
    {
        _repo = repo; _kb = kb; _paths = paths; _alerts = alerts; _dialogs = dialogs; _nav = nav; _backdrops = backdrops; _camFeed = camFeed;
        Palette = PlanElements.Groups.Select(g => new PaletteGroup(Loc.T("pal_" + g),
            (g == "devices" ? new[] { new PaletteItem("camera", Loc.T("pal_camera"), "◉") }
                            : PlanElements.InGroup(g).Select(e => new PaletteItem(e.Key, Loc.T((e.IsStructure ? "feat_" : "kind_") + e.Key), e.Glyph)).ToArray()))).ToList();
        var lang = Loc.Instance.Language;
        Plants = kb.Plantable.OrderBy(p => p.DisplayName(lang)).Select(p => new PlantOption(p.Id, p.DisplayName(lang))).ToList();
        Kinds = PlanElements.Objects.Select(e => e.Key).Append("structure").Select(k => new KindOption(k, Loc.T("kind_" + k)))
            .Concat(FeatureCatalog.Structures.Select(k => new KindOption(k, Loc.T("feat_" + k)))).ToList();
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
    public IReadOnlyList<PaletteGroup> Palette { get; }
    [ObservableProperty] private bool _paletteOpen;
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
    // Satellite backdrop: the picture, where it sits on the plan (cm) and whether it is shown.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BackdropVisible))] private System.Windows.Media.ImageSource? _backdrop;
    [ObservableProperty] private double _backdropX;
    [ObservableProperty] private double _backdropY;
    [ObservableProperty] private double _backdropW;
    [ObservableProperty] private double _backdropH;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BackdropVisible))] private bool _hasBackdrop;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BackdropVisible))] private bool _showBackdrop = true;
    public bool BackdropVisible => HasBackdrop && ShowBackdrop && Backdrop is not null;
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

    // ---- Property outline imported from the map page (3–20 pins) ----
    private List<Pt> _boundary = new();
    public bool JustImportedBoundary { get; set; }
    [ObservableProperty] private PointCollection _boundaryPoints = new();
    [ObservableProperty] private bool _hasBoundary;
    [ObservableProperty] private string _boundaryText = "";
    public double StrokeSize => 2.5 / Scale;

    public ObservableCollection<FeatureShape> FeatureShapes { get; } = new();
    public int? PlanFeatureId { get; set; }
    private List<Pt> _planAll = new();

    /// <summary>Places the property outline and every traced place on the plan, all on one plane so they keep their true positions.</summary>
    private PlanFrame? _frame;
    private GeoPoint[] _geoAll = Array.Empty<GeoPoint>();

    private void BuildBoundary(IReadOnlyList<PropertyFeature> features, ISet<int> hiddenFeatureIds)
    {
        _boundary = new(); _planAll = new(); BoundaryPoints = new PointCollection(); HasBoundary = false; BoundaryText = ""; FeatureShapes.Clear();
        _frame = null; Backdrop = null; HasBackdrop = false; _geoAll = Array.Empty<GeoPoint>();
        CanvasWidth = 1600; CanvasHeight = 1000;
        if (_garden is null) return;
        var outline = GeoMath.FromJson(_garden.BoundaryJson);
        var hasOutline = outline.Count >= GeoMath.MinBoundaryPins;
        // Structures drawn in the designer are editable beds; only the others are drawn here as locked outlines.
        var feats = features.Where(f => !hiddenFeatureIds.Contains(f.Id))
            .Select(f => (Feature: f, Pins: GeoMath.FromJson(f.PointsJson))).Where(x => x.Pins.Count >= GeoMath.MinBoundaryPins).ToList();
        var allKnown = features.SelectMany(f => GeoMath.FromJson(f.PointsJson)).ToList();
        if (hasOutline) allKnown.AddRange(outline);
        _geoAll = allKnown.ToArray();

        // One reversible frame for everything, so what is drawn here can be saved back as real coordinates.
        double w = 1600, h = 1000;
        if (allKnown.Count > 0) _frame = PlanFrame.Fit(allKnown, 500, out w, out h);
        else if (_garden.Latitude is { } la && _garden.Longitude is { } lo) _frame = new PlanFrame(new GeoPoint(la, lo), 800, 500);
        CanvasWidth = Math.Max(1600, w); CanvasHeight = Math.Max(1000, h);

        // The satellite backdrop may stick out beyond the shapes: widen the plane to hold it.
        if (_frame is not null && _garden.BackdropFile is { } file && _garden.BackdropTopLat is { } tla && _garden.BackdropTopLon is { } tlo && _garden.BackdropCmPerPixel is { } cmpp
            && _backdrops.Load(file) is BitmapSource img)
        {
            var tl = _frame.ToPlan(new GeoPoint(tla, tlo));
            var bw = img.PixelWidth * cmpp; var bh = img.PixelHeight * cmpp;
            var dx = tl.X < 0 ? -tl.X : 0; var dy = tl.Y < 0 ? -tl.Y : 0;
            if (dx > 0 || dy > 0) _frame.Shift(dx, dy);
            CanvasWidth = Math.Max(CanvasWidth + dx, tl.X + dx + bw); CanvasHeight = Math.Max(CanvasHeight + dy, tl.Y + dy + bh);
            Backdrop = img; BackdropX = tl.X + dx; BackdropY = tl.Y + dy; BackdropW = bw; BackdropH = bh; HasBackdrop = true;
        }

        var shapes = new List<IReadOnlyList<GeoPoint>>();
        if (hasOutline) shapes.Add(outline);
        shapes.AddRange(feats.Select(x => (IReadOnlyList<GeoPoint>)x.Pins));
        var plan = shapes.Select(s => (List<Pt>)s.Select(_frame!.ToPlan).ToList()).ToList();
        if (plan.Count == 0) return;
        _planAll = plan.SelectMany(s => s).ToList();
        var i = 0;
        if (hasOutline)
        {
            _boundary = plan[i++];
            BoundaryPoints = new PointCollection(_boundary.Select(p => new System.Windows.Point(p.X, p.Y)));
            var (area, per) = GeoMath.Measure(outline);
            BoundaryText = Loc.F("design_boundary", area.ToString("N0", Loc.Instance.Culture), per.ToString("N0", Loc.Instance.Culture));
            HasBoundary = true;
        }
        foreach (var (feature, pins) in feats)
        {
            var pts = plan[i++];
            var col = FeatureColors.For(feature.Kind);
            var fill = new SolidColorBrush(Color.FromArgb(FeatureCatalog.IsStructure(feature.Kind) ? (byte)150 : (byte)90, col.R, col.G, col.B)); fill.Freeze();
            var line = new SolidColorBrush(col); line.Freeze();
            FeatureShapes.Add(new FeatureShape(feature.Id, feature.Name, feature.Kind, Loc.T("feat_" + feature.Kind), FeatureCatalog.IsStructure(feature.Kind),
                new PointCollection(pts.Select(p => new System.Windows.Point(p.X, p.Y))), pts.Average(p => p.X), pts.Average(p => p.Y), fill, line,
                Loc.F("prop_area", GeoMath.Measure(pins).AreaM2.ToString("N0", Loc.Instance.Culture)), pts));
        }
    }

    [RelayCommand] private Task PlanSpaceAsync(FeatureShape f) => PlanFeatureCoreAsync(f.Id);

    /// <summary>Turns a traced garden space into a bed (its bounding rectangle) linked to the place; reuses the bed if it already exists.</summary>
    private Task PlanFeatureCoreAsync(int id) => RunAsync(async () =>
    {
        var fs = FeatureShapes.FirstOrDefault(x => x.Id == id);
        if (fs is null || fs.IsStructure || _garden is null) return;
        if (Beds.FirstOrDefault(b => b.Bed.FeatureId == id) is { } existing) { Selected = existing; return; }
        var minX = fs.Plan.Min(p => p.X); var minY = fs.Plan.Min(p => p.Y);
        var kind = fs.Kind switch { "greenhouse" => "greenhouse", "compost" => "compost", "pond" => "pond", "orchard" => "tree", _ => "bed" };
        var bed = await _repo.SaveAsync(new GardenBed
        {
            GardenId = _garden.Id, Name = fs.Name, Kind = kind, FeatureId = id, X = minX, Y = minY,
            Width = Math.Max(20, fs.Plan.Max(p => p.X) - minX), Height = Math.Max(20, fs.Plan.Max(p => p.Y) - minY),
        });
        var item = Wrap(bed);
        Beds.Add(item); _snapshots[bed.Id] = Capture(bed); Selected = item;
        History.Push(new UndoEntry(Loc.F("design_undo_add", item.Name), () => RemoveAsync(item), async () => item = await RecreateAsync(item)));
        StatusMessage = Loc.T("design_space_planned");
    }, busy: false);

    public double LabelSize => 13 / Scale;
    public double HandleSize => 16 / Scale;
    public double CameraIconSize => 28 / Scale;
    public double CameraIconOffset => -14 / Scale;
    public double CameraHandleSize => 16 / Scale;
    public System.Windows.Thickness CameraHandleMargin => new(-8 / Scale);
    public bool HasSelection => Selected is not null;
    public string ScaleText => Scale * 100 < 1 ? $"{Scale * 100:0.0}%" : $"{Scale * 100:0}%";
    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;
    public string UndoTip => History.UndoText is { } t ? Loc.F("design_undo_tip", t) : Loc.T("design_undo");
    public string RedoTip => History.RedoText is { } t ? Loc.F("design_redo_tip", t) : Loc.T("design_redo");
    public string StatusSelection => Selected is null ? Loc.T("design_status_none")
        : Loc.F("design_status_sel", Selected.Name, Selected.SizeText, Selected.AreaText, Selected.Rotation.ToString("0"),
            (Selected.X / 100).ToString("0.0", Loc.Instance.Culture), (Selected.Y / 100).ToString("0.0", Loc.Instance.Culture));

    partial void OnScaleChanged(double value) { OnPropertyChanged(nameof(StrokeSize)); OnPropertyChanged(nameof(LabelSize)); OnPropertyChanged(nameof(HandleSize)); OnPropertyChanged(nameof(CameraIconSize)); OnPropertyChanged(nameof(CameraIconOffset)); OnPropertyChanged(nameof(CameraHandleSize)); OnPropertyChanged(nameof(CameraHandleMargin)); OnPropertyChanged(nameof(ScaleText)); }
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

        var featureEntities = await _repo.ListAsync<PropertyFeature>(q => q.Where(f => f.GardenId == _garden.Id));
        var keep = Selected?.Id;
        var beds = await _repo.ListAsync<GardenBed>(q => q.Where(b => b.GardenId == _garden.Id).Include(b => b.Plantings).OrderBy(b => b.Id));
        var hidden = beds.Where(b => b.FeatureId is not null && FeatureCatalog.IsStructure(b.Kind)).Select(b => b.FeatureId!.Value).ToHashSet();
        BuildBoundary(featureEntities, hidden);
        await LoadCameraMarkersAsync();
        if (JustImportedBoundary) { StatusMessage = Loc.T("map_imported"); JustImportedBoundary = false; }
        FitView();      // open framed on the property, however large it is
        Beds.Clear(); _snapshots.Clear();
        foreach (var b in beds) { Beds.Add(Wrap(b)); _snapshots[b.Id] = Capture(b); }
        Selected = Beds.FirstOrDefault(b => b.Id == keep);
        BuildCards();
        RebuildCalendar();
        await RefreshAlertsAsync();
        if (PlanFeatureId is { } planId) { PlanFeatureId = null; await PlanFeatureCoreAsync(planId); }
    });

    private BedItem Wrap(GardenBed bed)
    {
        var item = new BedItem(bed) { PlantsText = PlantsSummary(bed.Plantings) };
        RebuildMarkers(item);
        return item;
    }

    private string PlantsSummary(IEnumerable<BedPlanting> plantings) =>
        string.Join(", ", plantings.Where(p => p.Status != PlantingStatus.Finished)
            .Select(p => $"{_kb.Get(p.PlantId)?.DisplayName(Loc.Instance.Language) ?? p.PlantId} ×{p.Quantity}"));

    // ---------------- Plant library ----------------
    private void BuildCards()
    {
        var lang = Loc.Instance.Language;
        _allCards = _kb.Plantable.OrderBy(p => p.DisplayName(lang)).Select(p =>
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
        if (bed.IsStructure) { StatusMessage = Loc.T("design_no_plants_in_structure"); return; }
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
        var parts = new List<string>();
        if (hit.Count > 0) parts.Add(Loc.F("design_overlap", string.Join(", ", hit)));
        // Only growing areas must stay inside the property; paths and structures may run to its edge.
        if (_boundary.Count >= 3 && !GeometryMath.PolygonInside(mine, _boundary)) parts.Add(Loc.T("design_outside"));
        // Permanent structures (house, workshop…) are not movable: a bed on top of one is a mistake worth flagging.
        var blocking = FeatureShapes.Where(f => f.IsStructure && Selected.Bed.FeatureId != f.Id && GeometryMath.RectOverlapsPolygon(mine, f.Plan)).Select(f => f.Name).ToList();
        if (blocking.Count > 0) parts.Add(Loc.F("design_overlap_structure", string.Join(", ", blocking)));
        OverlapText = string.Join(" · ", parts);
    }

    private async Task RefreshPlantingsAsync()
    {
        Plantings.Clear(); BedAlerts.Clear();
        if (Selected is null) return;
        var list = await _repo.ListAsync<BedPlanting>(q => q.Where(p => p.BedId == Selected.Id).OrderBy(p => p.Status).ThenBy(p => p.Id));
        Selected.Bed.Plantings = list;
        foreach (var p in list) Plantings.Add(Row(p));
        RebuildMarkers(Selected);
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
        var (shape, body) = PlantIcons.For(plant);
        var bodyBrush = new SolidColorBrush(body); bodyBrush.Freeze();
        var accentBrush = new SolidColorBrush(PlantIcons.AccentColorOf(shape)); accentBrush.Freeze();
        return new PlantingRow(p, plant?.DisplayName(Loc.Instance.Language) ?? p.PlantId, Loc.T("status_" + p.Status), expected,
            PlantIcons.BodyOf(shape), PlantIcons.AccentOf(shape), bodyBrush, accentBrush);
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
        RebuildMarkers(item);
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
        await SyncStructureAsync(bed);
    }, busy: false);

    /// <summary>A structure drawn here is also a place on the property: keep its map coordinates, name and kind in step.</summary>
    private async Task SyncStructureAsync(GardenBed bed)
    {
        if (bed.FeatureId is not { } fid || !FeatureCatalog.IsStructure(bed.Kind) || _frame is null) return;
        var feature = (await _repo.ListAsync<PropertyFeature>(q => q.Where(f => f.Id == fid))).FirstOrDefault();
        if (feature is null) return;
        feature.Name = bed.Name; feature.Kind = bed.Kind;
        feature.PointsJson = GeoMath.ToJson(GeometryMath.RectCorners(bed.X, bed.Y, bed.Width, bed.Height, bed.Rotation).Select(_frame.ToGeo).ToList());
        await _repo.SaveAsync(feature);
    }

    private static (double W, double H) StructureSize(string kind) => kind switch
    {
        "house" => (1000, 800), "workshop" => (500, 400), "shed" => (300, 300), "barn" => (1200, 600), "garage" => (600, 500), _ => (400, 400),
    };

    /// <summary>Adds whatever was picked in the palette in the middle of the view: a camera, a building, or a plan object such as a fence or a bin.</summary>
    [RelayCommand]
    private Task AddElementAsync(PaletteItem item)
    {
        PaletteOpen = false;
        if (item.Key == "camera") return AddCameraHereAsync();
        return PlanElements.Get(item.Key)?.IsStructure == true ? AddStructureAsync(item.Key) : AddObjectAsync(item.Key);
    }

    /// <summary>Adds a permanent structure (house, workshop, shed…) in the middle of the property; drag, resize and rotate it like a bed.</summary>
    [RelayCommand]
    private Task AddStructureAsync(string kind) => RunAsync(async () =>
    {
        if (_garden is null) return;
        var (w, h) = PlanElements.SizeOf(kind);
        var centre = NewObjectCentre();
        var offset = Beds.Count(b => b.IsStructure) * 120 % 600;
        double x = SnapValue(centre.X - w / 2 + offset), y = SnapValue(centre.Y - h / 2 + offset);
        var n = Beds.Count(b => b.Kind == kind) + 1;
        var name = $"{Loc.T("feat_" + kind)} {n}";
        // With a map position the building is also a place on the property; without one it is just a plan object (it can be linked later).
        int? featureId = null;
        if (_frame is not null)
        {
            var geo = GeometryMath.RectCorners(x, y, w, h, 0).Select(_frame.ToGeo).ToList();
            featureId = (await _repo.SaveAsync(new PropertyFeature { GardenId = _garden.Id, Name = name, Kind = kind, PointsJson = GeoMath.ToJson(geo) })).Id;
        }
        var bed = await _repo.SaveAsync(new GardenBed { GardenId = _garden.Id, Name = name, Kind = kind, FeatureId = featureId, X = x, Y = y, Width = w, Height = h });
        var item = Wrap(bed);
        Beds.Add(item); _snapshots[bed.Id] = Capture(bed); Selected = item;
        History.Push(new UndoEntry(Loc.F("design_undo_add", item.Name), () => RemoveAsync(item), async () => item = await RecreateAsync(item)));
        StatusMessage = Loc.T("design_structure_added");
    }, busy: false);

    /// <summary>Downloads (once, after asking) the satellite picture for the property area and shows it behind the plan.</summary>
    [RelayCommand]
    private Task LoadBackdropAsync() => RunAsync(async () =>
    {
        if (_garden is null) return;
        var area = _geoAll.ToList();
        if (area.Count == 0 && _garden.Latitude is { } la && _garden.Longitude is { } lo) area.Add(new GeoPoint(la, lo));
        if (area.Count == 0) { ErrorMessage = Loc.T("design_backdrop_need_geo"); return; }
        if (!_dialogs.Confirm(Loc.T("design_backdrop_confirm"))) return;
        IsBusy = true;
        try
        {
            var (file, grid) = await _backdrops.BuildAsync(_garden.Id, area);
            _garden.BackdropFile = file; _garden.BackdropTopLat = grid.TopLeft.Lat; _garden.BackdropTopLon = grid.TopLeft.Lon; _garden.BackdropCmPerPixel = grid.CmPerPixel;
            _garden = await _repo.SaveAsync(_garden);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or NotSupportedException or IOException)
        {
            ErrorMessage = Loc.F("design_backdrop_failed", ex.Message); return;
        }
        finally { IsBusy = false; }
        ShowBackdrop = true;
        await LoadAsync();
        StatusMessage = Loc.T("design_backdrop_done");
    }, busy: false);

    public double SnapValue(double v) => Snap ? Math.Round(v / 10) * 10 : v;

    [RelayCommand]
    private Task AddObjectAsync(string kind) => RunAsync(async () =>
    {
        if (_garden is null) return;
        var (w, h) = kind == "structure" ? (300.0, 300.0) : PlanElements.SizeOf(kind);
        var offset = Beds.Count * 40 % 400;
        var centre = NewObjectCentre();
        var bed = await _repo.SaveAsync(new GardenBed
        {
            GardenId = _garden.Id, Kind = kind, Name = $"{Loc.T("kind_" + kind)} {Beds.Count(b => b.Kind == kind) + 1}",
            X = SnapValue(centre.X - w / 2 + offset), Y = SnapValue(centre.Y - h / 2 + offset), Width = w, Height = h,
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
        // A structure is also a place on the property: removing it here removes the place too (undo brings both back).
        if (item.Bed.FeatureId is { } fid && item.IsStructure) await _repo.DeleteAsync(new PropertyFeature { Id = fid });
        if (Selected == item) Selected = null;
        Beds.Remove(item); _snapshots.Remove(item.Id);
        RebuildCalendar();
    }

    /// <summary>Recreates a deleted bed with its plantings (new database ids).</summary>
    private async Task<BedItem> RecreateAsync(BedItem old)
    {
        var o = old.Bed;
        var featureId = o.FeatureId;
        if (old.IsStructure && _frame is not null)
        {
            var geo = GeometryMath.RectCorners(o.X, o.Y, o.Width, o.Height, o.Rotation).Select(_frame.ToGeo).ToList();
            featureId = (await _repo.SaveAsync(new PropertyFeature { GardenId = o.GardenId, Name = o.Name, Kind = o.Kind, PointsJson = GeoMath.ToJson(geo) })).Id;
        }
        var copy = new GardenBed
        {
            GardenId = o.GardenId, Kind = o.Kind, Name = o.Name, X = o.X, Y = o.Y, Width = o.Width, Height = o.Height, Rotation = o.Rotation,
            ShadePercent = o.ShadePercent, Notes = o.Notes, FeatureId = featureId,
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

    // ---- Zoom and framing. The view does the anchored zoom (it knows the scroll position); the model only asks. ----
    public const double MinScale = 0.002, MaxScale = 6;
    public double ViewportWidth { get; set; } = 800;
    public double ViewportHeight { get; set; } = 560;
    /// <summary>Asks the view to zoom by a factor around the centre of what is visible.</summary>
    public event Action<double>? ZoomRequested;
    /// <summary>Asks the view to scroll so this point of the plan is in the middle of the window.</summary>
    public event Action<double, double>? CenterRequested;

    [RelayCommand] private void ZoomIn() => ZoomRequested?.Invoke(1.25);
    [RelayCommand] private void ZoomOut() => ZoomRequested?.Invoke(1 / 1.25);
    [RelayCommand] private void Fit() => FitView();

    /// <summary>Frames the property outline and everything drawn on it; with nothing drawn, frames the whole plan.</summary>
    private void FitView()
    {
        var pts = _planAll.Concat(Beds.SelectMany(b => b.Corners)).ToList();
        double minX = 0, minY = 0, maxX = CanvasWidth, maxY = CanvasHeight;
        if (pts.Count > 0) { minX = pts.Min(p => p.X); maxX = pts.Max(p => p.X); minY = pts.Min(p => p.Y); maxY = pts.Max(p => p.Y); }
        var w = Math.Max(200, maxX - minX); var h = Math.Max(200, maxY - minY);
        Scale = Math.Clamp(Math.Round(Math.Min((ViewportWidth - 40) / w, (ViewportHeight - 40) / h), 4), MinScale, MaxScale);
        CenterRequested?.Invoke((minX + maxX) / 2, (minY + maxY) / 2);
    }

    /// <summary>Where new things appear: the middle of the property, or of the plan when nothing is drawn yet.</summary>
    private void RequestCenter(double x, double y) => CenterRequested?.Invoke(x, y);

    private Pt NewObjectCentre()
    {
        if (_planAll.Count > 0) return new Pt((_planAll.Min(p => p.X) + _planAll.Max(p => p.X)) / 2, (_planAll.Min(p => p.Y) + _planAll.Max(p => p.Y)) / 2);
        return new Pt(CanvasWidth / 2, CanvasHeight / 2);
    }
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
