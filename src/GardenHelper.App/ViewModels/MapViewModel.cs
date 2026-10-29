using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Services;
using GardenHelper.Core.Site;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record PinItem(int Number, GeoPoint Point)
{
    public string Text => $"{Number}.  {Point.Lat.ToString("0.00000", CultureInfo.InvariantCulture)}, {Point.Lon.ToString("0.00000", CultureInfo.InvariantCulture)}";
    public override string ToString() => Text;
}

public sealed record KindOption2(string Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Colours used for each kind of place on every map and plan, so a greenhouse always looks like a greenhouse.</summary>
public static class FeatureColors
{
    public static Color For(string kind) => kind switch
    {
        "outline" => Color.FromRgb(0xD9, 0xA4, 0x00),
        "house" => Color.FromRgb(0xB0, 0x6A, 0x4A), "workshop" => Color.FromRgb(0x8C, 0x6F, 0x56), "shed" => Color.FromRgb(0x9A, 0x84, 0x6E),
        "barn" => Color.FromRgb(0x9B, 0x4F, 0x3A), "garage" => Color.FromRgb(0x77, 0x77, 0x82), "other_structure" => Color.FromRgb(0x88, 0x88, 0x88),
        "greenhouse" => Color.FromRgb(0x4F, 0xB5, 0xC4), "compost" => Color.FromRgb(0x7A, 0x56, 0x2E), "orchard" => Color.FromRgb(0x3E, 0x8E, 0x4C),
        "pond" => Color.FromRgb(0x3F, 0x7F, 0xC8), "free_land" => Color.FromRgb(0xB9, 0xC2, 0x6B), "other_space" => Color.FromRgb(0x9B, 0xB0, 0x7C),
        _ => Color.FromRgb(0x6F, 0xA8, 0x45),   // garden
    };
}

/// <summary>One traced shape on the map: the property outline, a structure, or a garden space.</summary>
public sealed partial class ShapeItem : ObservableObject
{
    public int Id { get; set; }
    public bool IsOutline { get; init; }
    public List<GeoPoint> Points { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Display))] private string _name = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Display)), NotifyPropertyChangedFor(nameof(KindLabel)), NotifyPropertyChangedFor(nameof(IsStructure))] private string _kind = "garden";
    [ObservableProperty] private string _notes = "";

    public bool IsStructure => FeatureCatalog.IsStructure(Kind);
    public string KindLabel => IsOutline ? Loc.T("feat_outline") : Loc.T("feat_" + Kind);
    public string Display => $"{Name} · {KindLabel}";
    public override string ToString() => Display;
}

/// <summary>Satellite map where the user traces the property outline, structures and garden spaces with pins, names them, and imports them into the designer.</summary>
public sealed partial class MapViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly SatelliteTileService _tiles;
    private readonly IDialogs _dialogs;
    private readonly INavigator _nav;
    private readonly Dictionary<(int Z, int X, int Y), ImageSource> _cache = new();
    private readonly HashSet<(int, int, int)> _pending = new();
    private Garden _garden = new() { Name = "" };
    private static readonly List<GeoPoint> NoPoints = new();

    public MapViewModel(Repository repo, SatelliteTileService tiles, IDialogs dialogs, INavigator nav, ILogger<MapViewModel> log) : base(log)
    {
        _repo = repo; _tiles = tiles; _dialogs = dialogs; _nav = nav;
        var all = FeatureCatalog.Structures.Concat(FeatureCatalog.Spaces);
        Kinds = all.Select(k => new KindOption2(k, Loc.T("feat_" + k))).ToList();
    }

    public IReadOnlyList<KindOption2> Kinds { get; }
    public ObservableCollection<ShapeItem> Shapes { get; } = new();
    public ObservableCollection<ShapeItem> Visible { get; } = new();
    public ObservableCollection<PinItem> Pins { get; } = new();
    /// <summary>Pins of the shape being edited.</summary>
    public IReadOnlyList<GeoPoint> Points => Selected?.Points ?? NoPoints;
    public int? PendingSelect { get; set; }
    /// <summary>Raised whenever the map must be redrawn (pins, centre or zoom changed, or a tile arrived).</summary>
    public event Action? MapChanged;

    [ObservableProperty] private double _centerLat = 45.9432;
    [ObservableProperty] private double _centerLon = 24.9668;
    [ObservableProperty] private int _zoom = 7;
    [ObservableProperty] private bool _mapEnabled;
    [ObservableProperty] private string _latitude = "";
    [ObservableProperty] private string _longitude = "";
    [ObservableProperty] private string _pinsText = "";
    [ObservableProperty] private string _areaText = "";
    [ObservableProperty] private string _warningText = "";
    [ObservableProperty] private bool _canImport;
    [ObservableProperty] private string _cursorText = "";
    [ObservableProperty] private string _tileProblem = "";
    [ObservableProperty] private ShapeItem? _selected;
    [ObservableProperty] private KindOption2? _selectedKind;
    [ObservableProperty] private string _shapeFilter = "";
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private bool _hasOutline;
    [ObservableProperty] private bool _canOpenModule;

    public string Attribution => SatelliteTileService.Attribution;

    partial void OnZoomChanged(int value) => MapChanged?.Invoke();
    partial void OnCenterLatChanged(double value) => MapChanged?.Invoke();
    partial void OnCenterLonChanged(double value) => MapChanged?.Invoke();
    partial void OnShapeFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedChanged(ShapeItem? oldValue, ShapeItem? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnShapePropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnShapePropertyChanged;
        HasSelection = newValue is not null;
        SelectedKind = newValue is null || newValue.IsOutline ? null : Kinds.FirstOrDefault(k => k.Key == newValue.Kind);
        Refresh();
    }

    partial void OnSelectedKindChanged(KindOption2? value)
    {
        if (Selected is { IsOutline: false } s && value is not null && s.Kind != value.Key) s.Kind = value.Key;
    }

    private void OnShapePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => MapChanged?.Invoke();

    private void ApplyFilter()
    {
        var f = Core.Library.SubjectExtractor.Fold(ShapeFilter ?? "");
        Visible.Clear();
        foreach (var s in Shapes.Where(s => f.Length == 0 || Core.Library.SubjectExtractor.Fold(s.Name + " " + s.KindLabel + " " + s.Notes).Contains(f)))
            Visible.Add(s);
    }

    public override Task LoadAsync() => RunAsync(async () =>
    {
        _garden = await _repo.ActiveGardenAsync() ?? new Garden { Name = "" };
        Shapes.Clear();
        var outline = GeoMath.FromJson(_garden.BoundaryJson);
        if (outline.Count > 0)
        {
            var o = new ShapeItem { IsOutline = true, Name = Loc.T("feat_outline"), Kind = "outline" };
            o.Points.AddRange(outline); Shapes.Add(o);
        }
        if (_garden.Id != 0)
            foreach (var f in await _repo.ListAsync<PropertyFeature>(q => q.Where(x => x.GardenId == _garden.Id).OrderBy(x => x.Name)))
            {
                var s = new ShapeItem { Id = f.Id, Name = f.Name, Kind = f.Kind, Notes = f.Notes ?? "" };
                s.Points.AddRange(GeoMath.FromJson(f.PointsJson)); Shapes.Add(s);
            }
        HasOutline = Shapes.Any(s => s.IsOutline);
        var withPoints = Shapes.Where(s => s.Points.Count > 0).SelectMany(s => s.Points).ToList();
        if (withPoints.Count > 0) { var c = GeoMath.Centroid(withPoints); CenterLat = c.Lat; CenterLon = c.Lon; Zoom = 18; }
        else if (_garden.Latitude is { } la && _garden.Longitude is { } lo) { CenterLat = la; CenterLon = lo; Zoom = 18; }
        Latitude = CenterLat.ToString("0.#####", CultureInfo.InvariantCulture);
        Longitude = CenterLon.ToString("0.#####", CultureInfo.InvariantCulture);
        ApplyFilter();
        Selected = PendingSelect is { } id ? Shapes.FirstOrDefault(s => s.Id == id) : null;
        PendingSelect = null;
        Refresh();
    });

    private void Refresh()
    {
        Pins.Clear();
        var pts = Points;
        for (var i = 0; i < pts.Count; i++) Pins.Add(new PinItem(i + 1, pts[i]));
        PinsText = Loc.F("map_pins", pts.Count, GeoMath.MaxBoundaryPins);
        var (area, perimeter) = GeoMath.Measure(pts);
        var crosses = GeoMath.SelfIntersects(pts);
        AreaText = Selected is null ? Loc.T("map_select_first")
            : pts.Count < GeoMath.MinBoundaryPins ? Loc.T("map_need_pins")
            : Loc.F("map_area", area.ToString("N0", Loc.Instance.Culture), (area / 100).ToString("N1", Loc.Instance.Culture), perimeter.ToString("N0", Loc.Instance.Culture));
        WarningText = crosses ? Loc.T("map_crosses") : Zoom < 17 && pts.Count > 0 ? Loc.T("map_zoom_hint") : "";
        CanImport = Shapes.Any(s => s.Points.Count >= GeoMath.MinBoundaryPins && !GeoMath.SelfIntersects(s.Points));
        CanOpenModule = Selected is { IsOutline: false };
        MapChanged?.Invoke();
    }

    [RelayCommand]
    private void LoadMap()
    {
        if (!_dialogs.Confirm(Loc.T("map_load_confirm"))) return;
        MapEnabled = true;
        MapChanged?.Invoke();
    }

    [RelayCommand]
    private void GoToCoordinates()
    {
        static bool P(string s, out double v) => double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        if (!P(Latitude, out var lat) || !P(Longitude, out var lon) || lat is < -85 or > 85 || lon is < -180 or > 180)
        { ErrorMessage = Loc.T("site_bad_coords"); return; }
        ErrorMessage = null; CenterLat = lat; CenterLon = lon; Zoom = 18;
    }

    [RelayCommand] private void ZoomIn() => Zoom = Math.Min(19, Zoom + 1);
    [RelayCommand] private void ZoomOut() => Zoom = Math.Max(3, Zoom - 1);

    // ---------------- Shapes ----------------
    [RelayCommand]
    private void NewShape(string kind)
    {
        var n = Shapes.Count(s => s.Kind == kind) + 1;
        var shape = new ShapeItem { Kind = kind, Name = $"{Loc.T("feat_" + kind)} {n}" };
        Shapes.Add(shape); ApplyFilter(); Selected = shape;
        StatusMessage = Loc.T("map_trace_hint");
    }

    [RelayCommand]
    private void NewOutline()
    {
        if (Shapes.Any(s => s.IsOutline)) { Selected = Shapes.First(s => s.IsOutline); return; }
        var shape = new ShapeItem { IsOutline = true, Kind = "outline", Name = Loc.T("feat_outline") };
        Shapes.Add(shape); HasOutline = true; ApplyFilter(); Selected = shape;
        StatusMessage = Loc.T("map_trace_hint");
    }

    [RelayCommand] private void SelectShape(ShapeItem shape) => Selected = shape;

    [RelayCommand]
    private Task DeleteShapeAsync() => RunAsync(async () =>
    {
        if (Selected is not { } s || !_dialogs.Confirm(Loc.F("map_delete_confirm", s.Name))) return;
        if (s.IsOutline) { _garden.BoundaryJson = null; if (_garden.Id != 0) _garden = await _repo.SaveAsync(_garden); HasOutline = false; }
        else if (s.Id != 0) await _repo.DeleteAsync(new PropertyFeature { Id = s.Id });
        Shapes.Remove(s); Selected = null; ApplyFilter(); Refresh();
    }, busy: false);

    // ---------------- Pins ----------------
    /// <summary>Adds a pin to the selected shape, or explains that a place must be chosen first.</summary>
    public void AddPin(GeoPoint p)
    {
        if (Selected is null) { StatusMessage = Loc.T("map_select_first"); return; }
        if (Selected.Points.Count >= GeoMath.MaxBoundaryPins) { StatusMessage = Loc.F("map_limit", GeoMath.MaxBoundaryPins); return; }
        StatusMessage = null;
        Selected.Points.Add(p); Refresh();
    }

    [RelayCommand] private void AddPinAtCenter() => AddPin(new GeoPoint(CenterLat, CenterLon));

    public void MovePin(int index, GeoPoint p)
    {
        if (Selected is null || index < 0 || index >= Selected.Points.Count) return;
        Selected.Points[index] = p; Refresh();
    }

    public void RemovePinAt(int index)
    {
        if (Selected is null || index < 0 || index >= Selected.Points.Count) return;
        Selected.Points.RemoveAt(index); Refresh();
    }

    [RelayCommand] private void RemovePin(PinItem pin) => RemovePinAt(pin.Number - 1);
    [RelayCommand] private void UndoPin() => RemovePinAt(Points.Count - 1);

    [RelayCommand]
    private void ClearPins()
    {
        if (Selected is null || Selected.Points.Count == 0 || !_dialogs.Confirm(Loc.T("map_clear_confirm"))) return;
        Selected.Points.Clear(); Refresh();
    }

    // ---------------- Saving ----------------
    private async Task<int> SaveAllCoreAsync()
    {
        if (string.IsNullOrWhiteSpace(_garden.Name)) _garden.Name = Loc.T("site_default_name");
        var outline = Shapes.FirstOrDefault(s => s.IsOutline);
        _garden.BoundaryJson = outline is { Points.Count: > 0 } ? GeoMath.ToJson(outline.Points) : null;
        var all = Shapes.SelectMany(s => s.Points).ToList();
        if (all.Count > 0 && _garden.Latitude is null) { var c = GeoMath.Centroid(all); _garden.Latitude = c.Lat; _garden.Longitude = c.Lon; }
        _garden.IsActive = true;
        _garden = await _repo.SaveAsync(_garden);

        var skipped = 0;
        foreach (var s in Shapes.Where(s => !s.IsOutline))
        {
            if (s.Points.Count < GeoMath.MinBoundaryPins || string.IsNullOrWhiteSpace(s.Name)) { skipped++; continue; }
            var saved = await _repo.SaveAsync(new PropertyFeature
            {
                Id = s.Id, GardenId = _garden.Id, Name = s.Name.Trim(), Kind = s.Kind, Notes = string.IsNullOrWhiteSpace(s.Notes) ? null : s.Notes,
                PointsJson = GeoMath.ToJson(s.Points), CreatedUtc = DateTime.UtcNow,
            });
            s.Id = saved.Id;
        }
        return skipped;
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        var skipped = await SaveAllCoreAsync();
        StatusMessage = skipped > 0 ? Loc.F("map_saved_skipped", skipped) : Loc.T("map_saved");
    }, busy: false);

    [RelayCommand]
    private Task ImportAsync() => RunAsync(async () =>
    {
        if (!CanImport) return;
        await SaveAllCoreAsync();
        var hasOutline = Shapes.Any(s => s.IsOutline && s.Points.Count >= GeoMath.MinBoundaryPins);
        _nav.GoTo<DesignerViewModel>(vm => vm.JustImportedBoundary = hasOutline || Shapes.Count > 0);
    }, busy: false);

    [RelayCommand]
    private Task OpenModuleAsync() => RunAsync(async () =>
    {
        if (Selected is not { IsOutline: false } s) return;
        await SaveAllCoreAsync();
        if (s.Id == 0) { StatusMessage = Loc.T("map_need_pins"); return; }
        var id = s.Id;
        _nav.GoTo<SpaceViewModel>(vm => vm.FeatureId = id);
    }, busy: false);

    // ---------------- Tiles ----------------
    public ImageSource? TryGetCachedTile(int z, int x, int y) => _cache.GetValueOrDefault((z, x, y));

    /// <summary>Loads a tile in the background (once) and asks the view to redraw when it arrives.</summary>
    public void RequestTile(int z, int x, int y)
    {
        var key = (z, x, y);
        if (!MapEnabled || _cache.ContainsKey(key) || !_pending.Add(key)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await _tiles.FetchTileAsync(z, x, y);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = new MemoryStream(bytes); bmp.EndInit(); bmp.Freeze();
                    if (_cache.Count > 600) _cache.Clear();
                    _cache[key] = bmp; TileProblem = "";
                    MapChanged?.Invoke();
                });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException or IOException)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => TileProblem = Loc.F("map_tile_failed", ex.Message));
            }
            finally { _pending.Remove(key); }
        });
    }
}
