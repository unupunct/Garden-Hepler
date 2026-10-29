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

/// <summary>Satellite map where the user drops 3–20 pins to outline the property, then imports the outline into the designer.</summary>
public sealed partial class MapViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly SatelliteTileService _tiles;
    private readonly IDialogs _dialogs;
    private readonly INavigator _nav;
    private readonly List<GeoPoint> _points = new();
    private readonly Dictionary<(int Z, int X, int Y), ImageSource> _cache = new();
    private readonly HashSet<(int, int, int)> _pending = new();
    private Garden _garden = new() { Name = "" };

    public MapViewModel(Repository repo, SatelliteTileService tiles, IDialogs dialogs, INavigator nav, ILogger<MapViewModel> log) : base(log)
    {
        _repo = repo; _tiles = tiles; _dialogs = dialogs; _nav = nav;
    }

    public ObservableCollection<PinItem> Pins { get; } = new();
    public IReadOnlyList<GeoPoint> Points => _points;
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

    public string Attribution => SatelliteTileService.Attribution;

    partial void OnZoomChanged(int value) => MapChanged?.Invoke();
    partial void OnCenterLatChanged(double value) => MapChanged?.Invoke();
    partial void OnCenterLonChanged(double value) => MapChanged?.Invoke();

    public override Task LoadAsync() => RunAsync(async () =>
    {
        _garden = await _repo.ActiveGardenAsync() ?? new Garden { Name = "" };
        _points.Clear();
        _points.AddRange(GeoMath.FromJson(_garden.BoundaryJson));
        if (_points.Count > 0) { var c = GeoMath.Centroid(_points); CenterLat = c.Lat; CenterLon = c.Lon; Zoom = 18; }
        else if (_garden.Latitude is { } la && _garden.Longitude is { } lo) { CenterLat = la; CenterLon = lo; Zoom = 18; }
        Latitude = CenterLat.ToString("0.#####", CultureInfo.InvariantCulture);
        Longitude = CenterLon.ToString("0.#####", CultureInfo.InvariantCulture);
        Refresh();
    });

    private void Refresh()
    {
        Pins.Clear();
        for (var i = 0; i < _points.Count; i++) Pins.Add(new PinItem(i + 1, _points[i]));
        PinsText = Loc.F("map_pins", _points.Count, GeoMath.MaxBoundaryPins);
        var (area, perimeter) = GeoMath.Measure(_points);
        var crosses = GeoMath.SelfIntersects(_points);
        AreaText = _points.Count < GeoMath.MinBoundaryPins ? Loc.T("map_need_pins")
            : Loc.F("map_area", area.ToString("N0", Loc.Instance.Culture), (area / 100).ToString("N1", Loc.Instance.Culture), perimeter.ToString("N0", Loc.Instance.Culture));
        WarningText = crosses ? Loc.T("map_crosses") : Zoom < 17 && _points.Count > 0 ? Loc.T("map_zoom_hint") : "";
        CanImport = _points.Count >= GeoMath.MinBoundaryPins && !crosses;
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

    /// <summary>Adds a pin at the map centre: a keyboard-friendly alternative to clicking.</summary>
    [RelayCommand] private void AddPinAtCenter() => AddPin(new GeoPoint(CenterLat, CenterLon));

    public void AddPin(GeoPoint p)
    {
        if (_points.Count >= GeoMath.MaxBoundaryPins) { StatusMessage = Loc.F("map_limit", GeoMath.MaxBoundaryPins); return; }
        StatusMessage = null;
        _points.Add(p); Refresh();
    }

    public void MovePin(int index, GeoPoint p)
    {
        if (index < 0 || index >= _points.Count) return;
        _points[index] = p; Refresh();
    }

    public void RemovePinAt(int index)
    {
        if (index < 0 || index >= _points.Count) return;
        _points.RemoveAt(index); Refresh();
    }

    [RelayCommand] private void RemovePin(PinItem pin) => RemovePinAt(pin.Number - 1);
    [RelayCommand] private void UndoPin() => RemovePinAt(_points.Count - 1);

    [RelayCommand]
    private void Clear()
    {
        if (_points.Count == 0 || !_dialogs.Confirm(Loc.T("map_clear_confirm"))) return;
        _points.Clear(); Refresh();
    }

    private async Task<bool> SaveCoreAsync()
    {
        if (string.IsNullOrWhiteSpace(_garden.Name)) _garden.Name = Loc.T("site_default_name");
        _garden.BoundaryJson = _points.Count == 0 ? null : GeoMath.ToJson(_points);
        if (_points.Count >= GeoMath.MinBoundaryPins && _garden.Latitude is null)
        {
            var c = GeoMath.Centroid(_points); _garden.Latitude = c.Lat; _garden.Longitude = c.Lon;
        }
        _garden.IsActive = true;
        _garden = await _repo.SaveAsync(_garden);
        return true;
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () => { await SaveCoreAsync(); StatusMessage = Loc.T("map_saved"); }, busy: false);

    [RelayCommand]
    private Task ImportAsync() => RunAsync(async () =>
    {
        if (!CanImport) return;
        await SaveCoreAsync();
        _nav.GoTo<DesignerViewModel>(vm => vm.JustImportedBoundary = true);
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
