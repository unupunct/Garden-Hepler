using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Design;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Services;
using GardenHelper.Core.Site;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

/// <summary>One thing on the floor plan, movable by dragging.</summary>
public sealed partial class InteriorShape : ObservableObject
{
    public InteriorShape(InteriorItem item, InventoryPlant? plant, DateOnly today)
    {
        Item = item; Plant = plant;
        var kind = InteriorCatalog.Get(item.Kind);
        Layer = kind?.Layer ?? 1; Glyph = kind?.Glyph ?? "•";
        var c = (Color)ColorConverter.ConvertFromString(kind?.Color ?? "#CCCCCC");
        Fill = Frozen(Color.FromArgb(item.Kind == "room" ? (byte)0xB0 : (byte)0xFF, c.R, c.G, c.B));
        Edge = Frozen(Color.FromRgb((byte)(c.R * 0.62), (byte)(c.G * 0.62), (byte)(c.B * 0.62)));
        Due = plant is not null && InventoryRules.DaysOverdue(plant, today) is { } d && d >= 0;
        Needs = plant is not null && (plant.Status is "attention" or "recovering");
    }

    public InteriorItem Item { get; }
    public InventoryPlant? Plant { get; }
    public int Layer { get; }
    public string Glyph { get; }
    public Brush Fill { get; }
    public Brush Edge { get; }
    public bool Due { get; }
    public bool Needs { get; }
    public bool IsPlant => Item.Kind == "plant";
    public bool IsRound => InteriorCatalog.IsRound(Item.Kind);
    public bool IsWall => Item.Kind == "wall";
    public bool IsOpening => InteriorCatalog.IsOpening(Item.Kind);
    public bool IsRoom => Item.Kind == "room";
    public bool ShowLabel => !IsWall && !IsOpening && !IsRoom;
    public bool ShowGlyph => !IsWall && !IsOpening && !IsRoom;
    public double X { get => Item.X; set { Item.X = value; OnPropertyChanged(); } }
    public double Y { get => Item.Y; set { Item.Y = value; OnPropertyChanged(); } }
    public double Width { get => Item.Width; set { Item.Width = Math.Max(10, value); OnPropertyChanged(); } }
    public double Height { get => Item.Height; set { Item.Height = Math.Max(5, value); OnPropertyChanged(); } }
    public double Rotation { get => Item.Rotation; set { Item.Rotation = value; OnPropertyChanged(); } }
    public string Label => !string.IsNullOrWhiteSpace(Item.Name) ? Item.Name : Plant?.Name ?? Loc.T("int_k_" + Item.Kind);
    public void Refresh() { OnPropertyChanged(nameof(Width)); OnPropertyChanged(nameof(Height)); OnPropertyChanged(nameof(Label)); OnPropertyChanged(nameof(X)); OnPropertyChanged(nameof(Y)); OnPropertyChanged(nameof(Rotation)); }
    public string Tip
    {
        get
        {
            if (Plant is null) return Label;
            var next = InventoryRules.NextWatering(Plant);
            return $"{Plant.Name} ({Loc.T("inv_st_" + Plant.Status)})" + (next is { } n ? "\n" + Loc.F("int_water_next", n.ToString("d", Loc.Instance.Culture)) : "") + (string.IsNullOrWhiteSpace(Plant.Notes) ? "" : "\n" + Plant.Notes);
        }
    }
    [ObservableProperty] private bool _isSelected;
    public static double SnapSize(double v) => Math.Round(v / 5) * 5;
    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}

public sealed record InteriorToolGroup(string Title, IReadOnlyList<InteriorKind> Kinds);
public sealed record FloorOption(int Floor, string Label) { public override string ToString() => Label; }
public sealed record PlantChoice(int? Id, string Name) { public override string ToString() => Name; }

/// <summary>Floor plans of the buildings: draw walls, windows and doors, set out the rooms and furniture, and put each indoor plant where it really stands.</summary>
public sealed partial class InteriorViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly IDialogs _dialogs;
    private readonly INavigator _nav;
    private readonly ILogger _logger;
    private bool _loading;
    private List<InteriorItem> _all = new();
    private List<InventoryPlant> _plants = new();

    public InteriorViewModel(Repository repo, IDialogs dialogs, INavigator nav, ILogger<InteriorViewModel> log) : base(log)
    {
        _logger = log; _repo = repo; _dialogs = dialogs; _nav = nav;
        Floors = InteriorCatalog.Floors.Select(f => new FloorOption(f, Loc.T("int_floor_" + InteriorCatalog.FloorKey(f)))).ToList();
        _floor = Floors[1];
        Tools = InteriorCatalog.Groups.Select(g => new InteriorToolGroup(Loc.T("int_g_" + g), InteriorCatalog.InGroup(g).ToList())).ToList();
    }

    /// <summary>The building to open (set by pages that link here).</summary>
    public int FeatureId { get; set; }

    public IReadOnlyList<FloorOption> Floors { get; }
    public IReadOnlyList<InteriorToolGroup> Tools { get; }
    public ObservableCollection<PlaceOption> Buildings { get; } = new();
    public ObservableCollection<InteriorShape> Shapes { get; } = new();
    public ObservableCollection<PlantChoice> PlantChoices { get; } = new();
    public ObservableCollection<PlantChoice> Unplaced { get; } = new();

    [ObservableProperty] private PlaceOption? _building;
    [ObservableProperty] private FloorOption _floor;
    [ObservableProperty] private bool _hasBuilding;
    [ObservableProperty] private double _canvasWidth = 1000;
    [ObservableProperty] private double _canvasHeight = 800;
    [ObservableProperty] private double _scale = 0.5;
    [ObservableProperty] private PointCollection _outline = new();
    [ObservableProperty] private bool _hasOutline;
    [ObservableProperty] private bool _drawWalls;
    [ObservableProperty] private InteriorShape? _selected;
    [ObservableProperty] private bool _hasSelected;
    [ObservableProperty] private bool _selectedIsPlant;
    [ObservableProperty] private string _itemName = "", _itemX = "", _itemY = "", _itemW = "", _itemH = "", _itemRot = "", _itemNotes = "";
    [ObservableProperty] private PlantChoice? _itemPlant;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _attention = "";
    [ObservableProperty] private bool _hasAttention;
    [ObservableProperty] private double _viewportWidth, _viewportHeight;

    // sizes that stay readable at any zoom
    public double LabelSize => Math.Clamp(13 / Math.Max(Scale, 0.05), 10, 60);
    public double HandleSize => Math.Clamp(16 / Math.Max(Scale, 0.05), 12, 80);
    public double ZoomPercent => Math.Round(Scale * 100);
    partial void OnScaleChanged(double value) { OnPropertyChanged(nameof(LabelSize)); OnPropertyChanged(nameof(HandleSize)); OnPropertyChanged(nameof(ZoomPercent)); }

    private static double? Num(string s) => double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    private int BuildingId => int.TryParse(Building?.Id, out var id) ? id : 0;
    private DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            _loading = true;
            var garden = await _repo.ActiveGardenAsync();
            Buildings.Clear(); string? houseId = null;
            if (garden is not null)
                foreach (var f in await _repo.ListAsync<PropertyFeature>(q => q.Where(x => x.GardenId == garden.Id).OrderBy(x => x.Name)))
                    if (FeatureCatalog.IsStructure(f.Kind) || f.Kind == "greenhouse") { Buildings.Add(new PlaceOption(f.Id.ToString(), f.Name)); if (f.Kind == "house") houseId ??= f.Id.ToString(); }
            HasBuilding = Buildings.Count > 0;
            var want = FeatureId > 0 ? FeatureId.ToString() : Building?.Id;
            Building = Buildings.FirstOrDefault(b => b.Id == want) ?? Buildings.FirstOrDefault(b => b.Id == houseId) ?? Buildings.FirstOrDefault();
            FeatureId = 0;
            _loading = false;
            await LoadBuildingAsync();
        });
    }

    partial void OnBuildingChanged(PlaceOption? value) { if (!_loading && value is not null) _ = LoadBuildingAsync(); }
    partial void OnFloorChanged(FloorOption value) { if (!_loading) { Rebuild(); FitView(); } }

    private async Task LoadBuildingAsync()
    {
        if (BuildingId == 0) return;
        var feature = (await _repo.ListAsync<PropertyFeature>(q => q.Where(f => f.Id == BuildingId))).FirstOrDefault();
        var pts = new List<GeoPoint>();
        try { pts = JsonSerializer.Deserialize<List<GeoPoint>>(feature?.PointsJson ?? "[]") ?? new(); } catch (JsonException) { }
        var (w, h) = InteriorCatalog.Footprint(pts);
        CanvasWidth = w; CanvasHeight = h;
        var oc = InteriorCatalog.OutlineCm(pts);
        Outline = new PointCollection(oc.Select(p => new Point(p.X, p.Y))); HasOutline = oc.Count >= 3;
        _all = await _repo.ListAsync<InteriorItem>(q => q.Where(i => i.FeatureId == BuildingId).OrderBy(i => i.Id));
        if (_all.Count == 0 && feature?.Kind == "greenhouse") await ImportGreenhouseItemsAsync();
        _plants = await _repo.ListAsync<InventoryPlant>(q => q.Where(p => !p.IsTree && p.Status != "dead").OrderBy(p => p.Name));
        Rebuild(); FitView();
    }

    /// <summary>Layouts drawn on the old greenhouse page move over to the plan editor, once.</summary>
    private async Task ImportGreenhouseItemsAsync()
    {
        var old = await _repo.ListAsync<GardenHelper.Core.Greenhouse.GreenhouseItem>(q => q.Where(i => i.FeatureId == BuildingId).OrderBy(i => i.Id));
        foreach (var o in old)
        {
            var kind = InteriorCatalog.Get(o.Kind) is not null ? o.Kind : "custom";
            _all.Add(await _repo.SaveAsync(new InteriorItem { FeatureId = BuildingId, Floor = 0, Kind = kind, Name = o.Name, X = o.X + 50, Y = o.Y + 50, Width = o.Width, Height = o.Height, Notes = o.Notes }));
            await _repo.DeleteAsync(o);
        }
    }

    private void Rebuild()
    {
        var keepId = Selected?.Item.Id;
        Shapes.Clear();
        var byId = _plants.ToDictionary(p => p.Id);
        foreach (var i in _all.Where(i => i.Floor == Floor.Floor).OrderBy(i => InteriorCatalog.Get(i.Kind)?.Layer ?? 1).ThenBy(i => i.Id))
            Shapes.Add(new InteriorShape(i, i.InventoryPlantId is { } pid && byId.TryGetValue(pid, out var p) ? p : null, Today));
        Selected = keepId is { } k ? Shapes.FirstOrDefault(s => s.Item.Id == k) : null;
        PlantChoices.Clear(); PlantChoices.Add(new PlantChoice(null, Loc.T("int_no_plant")));
        foreach (var p in _plants.Where(p => p.Location == PlantLocation.Indoor || p.Location == PlantLocation.Greenhouse)) PlantChoices.Add(new PlantChoice(p.Id, p.Name));
        var placed = _all.Where(i => i.InventoryPlantId is not null).Select(i => i.InventoryPlantId!.Value).ToHashSet();
        Unplaced.Clear();
        foreach (var p in _plants.Where(p => p.Location == PlantLocation.Indoor && !placed.Contains(p.Id))) Unplaced.Add(new PlantChoice(p.Id, p.Name));
        var walls = Shapes.Count(s => s.IsWall); var plants = Shapes.Count(s => s.IsPlant);
        var rooms = Shapes.Count(s => s.IsRoom); var furniture = Shapes.Count(s => !s.IsWall && !s.IsOpening && !s.IsRoom && !s.IsPlant);
        Summary = Loc.F("int_summary", rooms, walls, Shapes.Count(s => s.IsOpening), furniture, plants);
        var due = Shapes.Where(s => s.Due).Select(s => s.Label).ToList(); var sick = Shapes.Where(s => s.Needs && !s.Due).Select(s => s.Label).ToList();
        var parts = new List<string>();
        if (due.Count > 0) parts.Add(Loc.F("int_due", string.Join(", ", due)));
        if (sick.Count > 0) parts.Add(Loc.F("int_sick", string.Join(", ", sick)));
        Attention = string.Join("  ", parts); HasAttention = parts.Count > 0;
    }

    /// <summary>Zoom so the whole floor fits the window (and no bigger than 100%).</summary>
    public void FitView()
    {
        if (ViewportWidth < 50 || ViewportHeight < 50) return;
        Scale = Math.Clamp(Math.Min((ViewportWidth - 24) / CanvasWidth, (ViewportHeight - 24) / CanvasHeight), 0.08, 1.0);
    }

    [RelayCommand] private void ZoomIn() => Scale = Math.Min(4, Scale * 1.25);
    [RelayCommand] private void ZoomOut() => Scale = Math.Max(0.08, Scale / 1.25);
    [RelayCommand] private void ZoomFit() => FitView();

    partial void OnSelectedChanged(InteriorShape? old, InteriorShape? value)
    {
        if (old is not null) old.IsSelected = false;
        HasSelected = value is not null; SelectedIsPlant = value?.IsPlant == true;
        if (value is null) return;
        value.IsSelected = true; FillFields(value);
    }

    private void FillFields(InteriorShape s)
    {
        ItemName = s.Item.Name; ItemX = F(Math.Round(s.X)); ItemY = F(Math.Round(s.Y)); ItemW = F(Math.Round(s.Width)); ItemH = F(Math.Round(s.Height)); ItemRot = F(s.Rotation); ItemNotes = s.Item.Notes ?? "";
        ItemPlant = PlantChoices.FirstOrDefault(p => p.Id == s.Item.InventoryPlantId) ?? PlantChoices.FirstOrDefault();
    }

    [RelayCommand]
    private Task AddKindAsync(InteriorKind kind) => RunAsync(async () =>
    {
        if (BuildingId == 0) return;
        var (w, h) = (kind.Width, kind.Height);
        // dropped in the middle of what is on screen
        var item = new InteriorItem { FeatureId = BuildingId, Floor = Floor.Floor, Kind = kind.Key, Width = w, Height = h, X = InteriorCatalog.Snap(CanvasWidth / 2 - w / 2), Y = InteriorCatalog.Snap(CanvasHeight / 2 - h / 2) };
        await AddAsync(item);
    }, busy: false);

    private async Task AddAsync(InteriorItem item)
    {
        item = await _repo.SaveAsync(item); _all.Add(item); Rebuild(); Selected = Shapes.FirstOrDefault(s => s.Item.Id == item.Id);
    }

    /// <summary>A wall drawn with the mouse from one point to another (snapped to 10 cm, straight when nearly so).</summary>
    public async Task DrawWallAsync(double x1, double y1, double x2, double y2)
    {
        x1 = InteriorCatalog.Snap(x1); y1 = InteriorCatalog.Snap(y1);
        var dx = x2 - x1; var dy = y2 - y1;
        if (Math.Abs(dx) < Math.Abs(dy) * 0.2) dx = 0; else if (Math.Abs(dy) < Math.Abs(dx) * 0.2) dy = 0;   // nearly straight: make it straight
        x2 = InteriorCatalog.Snap(x1 + dx); y2 = InteriorCatalog.Snap(y1 + dy);
        if (Math.Abs(x2 - x1) + Math.Abs(y2 - y1) < 30) return;
        var wall = InteriorCatalog.WallBetween(BuildingId, Floor.Floor, x1, y1, x2, y2);
        await AddAsync(wall);
    }

    [RelayCommand]
    private Task SaveItemAsync() => RunAsync(async () =>
    {
        if (Selected is not { } s) return;
        if (Num(ItemX) is not { } x || Num(ItemY) is not { } y || Num(ItemW) is not { } w || Num(ItemH) is not { } h || Num(ItemRot) is not { } r || w is < 10 or > 10000 || h is < 5 or > 10000)
        { ErrorMessage = Loc.T("int_bad_item"); return; }
        (s.X, s.Y, s.Width, s.Height, s.Rotation) = (x, y, w, h, ((r % 360) + 360) % 360);
        s.Item.Name = ItemName.Trim(); s.Item.Notes = string.IsNullOrWhiteSpace(ItemNotes) ? null : ItemNotes;
        if (s.IsPlant) s.Item.InventoryPlantId = ItemPlant?.Id;
        await _repo.SaveAsync(s.Item);
        Rebuild();
        StatusMessage = Loc.T("saved");
    }, busy: false);

    [RelayCommand]
    private Task DuplicateAsync() => RunAsync(async () =>
    {
        if (Selected is not { } s) return;
        await AddAsync(new InteriorItem { FeatureId = s.Item.FeatureId, Floor = s.Item.Floor, Kind = s.Item.Kind, Name = s.Item.Name, X = s.X + 40, Y = s.Y + 40, Width = s.Width, Height = s.Height, Rotation = s.Rotation });
    }, busy: false);

    [RelayCommand]
    private Task RotateAsync() => RunAsync(async () =>
    {
        if (Selected is not { } s) return;
        s.Rotation = (s.Rotation + 90) % 360; ItemRot = F(s.Rotation); await _repo.SaveAsync(s.Item);
    }, busy: false);

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (Selected is not { } s || !_dialogs.Confirm(Loc.F("int_delete", s.Label))) return;
        await _repo.DeleteAsync(s.Item); _all.Remove(s.Item); Selected = null; Rebuild();
    }, busy: false);

    /// <summary>Puts an indoor plant from the inventory on the plan, in the middle of the room, ready to be dragged to its spot.</summary>
    [RelayCommand]
    private Task PlacePlantAsync(PlantChoice plant) => RunAsync(async () =>
    {
        if (BuildingId == 0 || plant.Id is null) return;
        var (w, h) = InteriorCatalog.SizeOf("plant");
        await AddAsync(new InteriorItem { FeatureId = BuildingId, Floor = Floor.Floor, Kind = "plant", InventoryPlantId = plant.Id, Width = w, Height = h, X = InteriorCatalog.Snap(CanvasWidth / 2), Y = InteriorCatalog.Snap(CanvasHeight / 2) });
    }, busy: false);

    [RelayCommand]
    private void OpenInventory() => _nav.GoTo<InventoryViewModel>();

    /// <summary>Called when a drag ends: windows and doors jump onto the nearest wall, everything else snaps to the grid.</summary>
    public async Task MovedAsync(InteriorShape s)
    {
        if (s.IsOpening) { if (!InteriorCatalog.SnapToWall(s.Item, _all)) { s.X = InteriorCatalog.Snap(s.X, 5); s.Y = InteriorCatalog.Snap(s.Y, 5); } s.Refresh(); }
        else { s.X = InteriorCatalog.Snap(s.X, 5); s.Y = InteriorCatalog.Snap(s.Y, 5); }
        await _repo.SaveAsync(s.Item);
        if (Selected == s) FillFields(s);
    }

    public async Task ResizedAsync(InteriorShape s) { await _repo.SaveAsync(s.Item); if (Selected == s) FillFields(s); }
}
