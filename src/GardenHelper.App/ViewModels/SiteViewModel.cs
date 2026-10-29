using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Data;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Services;
using GardenHelper.Core.Site;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record SunRow(string Month, string Sunrise, string Sunset, string DayLength, string Elevation);
public sealed record ClimateRow(string Month, string Min, string Max, string Mean, string Gdd, string FrostDays);

public sealed partial class SiteViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly AppPaths _paths;
    private readonly ClimateService _climate;
    private readonly SatelliteTileService _tiles;
    private readonly IDialogs _dialogs;
    private Garden _garden = new() { Name = "" };
    private ClimateSummary? _summary;

    public SiteViewModel(Repository repo, AppPaths paths, ClimateService climate, SatelliteTileService tiles, IDialogs dialogs, ILogger<SiteViewModel> log) : base(log)
    {
        _repo = repo; _paths = paths; _climate = climate; _tiles = tiles; _dialogs = dialogs;
    }

    public ObservableCollection<SunRow> Sun { get; } = new();
    public ObservableCollection<ClimateRow> Months { get; } = new();

    [ObservableProperty] private string _latitude = "";
    [ObservableProperty] private string _longitude = "";
    [ObservableProperty] private string _zoom = "18";
    [ObservableProperty] private ImageSource? _satellite;
    [ObservableProperty] private string _scaleText = "";
    [ObservableProperty] private string _frostText = "";
    [ObservableProperty] private bool _hasClimate;
    [ObservableProperty] private bool _hasSun;
    [ObservableProperty] private bool _canApplyFrost;
    [ObservableProperty] private string _bestText = "";

    public string Attribution => SatelliteTileService.Attribution;

    public override Task LoadAsync() => RunAsync(async () =>
    {
        _garden = await _repo.ActiveGardenAsync() ?? new Garden { Name = "" };
        Latitude = _garden.Latitude?.ToString("0.#####", CultureInfo.InvariantCulture) ?? "";
        Longitude = _garden.Longitude?.ToString("0.#####", CultureInfo.InvariantCulture) ?? "";
        _summary = ClimateSummary.FromJson(_garden.ClimateJson);
        ShowClimate();
        LoadSatellite();
        if (TryCoordinates(out _, out _)) ComputeSun();
    });

    private bool TryCoordinates(out double lat, out double lon)
    {
        lat = lon = 0;
        static bool P(string s, out double v) => double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        return P(Latitude, out lat) && P(Longitude, out lon) && lat is >= -90 and <= 90 && lon is >= -180 and <= 180;
    }

    private async Task<bool> SaveCoordinatesAsync()
    {
        if (!TryCoordinates(out var lat, out var lon)) { ErrorMessage = Loc.T("site_bad_coords"); return false; }
        if (string.IsNullOrWhiteSpace(_garden.Name)) _garden.Name = Loc.T("site_default_name");
        _garden.Latitude = lat; _garden.Longitude = lon; _garden.IsActive = true;
        _garden = await _repo.SaveAsync(_garden);
        return true;
    }

    [RelayCommand]
    private Task CalculateSunAsync() => RunAsync(async () =>
    {
        if (!await SaveCoordinatesAsync()) return;
        ComputeSun();
        StatusMessage = Loc.T("saved");
    }, busy: false);

    private void ComputeSun()
    {
        if (!TryCoordinates(out var lat, out var lon)) return;
        var year = DateTime.Today.Year;
        var offset = SolarCalculator.GuessOffset(lon, new DateOnly(year, 6, 21));
        var c = Loc.Instance.Culture;
        Sun.Clear();
        foreach (var d in SolarCalculator.Year(lat, lon, year, offset))
            Sun.Add(new SunRow(d.Date.ToString("MMMM", c), d.Sunrise?.ToString("HH:mm") ?? "–", d.Sunset?.ToString("HH:mm") ?? "–",
                $"{d.DayLengthHours:0.0} h", $"{d.NoonElevationDeg:0}°"));
        HasSun = true;
        var best = SolarCalculator.Day(lat, lon, new DateOnly(year, 6, 21), offset);
        var worst = SolarCalculator.Day(lat, lon, new DateOnly(year, 12, 21), offset);
        BestText = Loc.F("site_sun_summary", best.DayLengthHours.ToString("0.0", c), worst.DayLengthHours.ToString("0.0", c),
            lat >= 0 ? Loc.T("site_south") : Loc.T("site_north"));
    }

    [RelayCommand]
    private Task FetchOnlineAsync() => RunAsync(async () =>
    {
        if (!await SaveCoordinatesAsync()) return;
        if (!_dialogs.Confirm(Loc.T("site_online_confirm"))) return;
        TryCoordinates(out var lat, out var lon);
        var zoom = int.TryParse(Zoom, out var z) ? Math.Clamp(z, 15, 19) : 18;

        var tilesTask = _tiles.FetchBlockAsync(lat, lon, zoom);
        var climateTask = _climate.FetchAsync(lat, lon);
        string? problem = null;
        try { _summary = await climateTask; }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException or TaskCanceledException)
        { problem = Loc.F("site_climate_failed", ex.Message); }

        try
        {
            var blocks = await tilesTask;
            var file = $"site-{_garden.Id}.jpg";
            BuildImage(blocks, lat, lon, zoom, Path.Combine(_paths.SiteDir, file));
            _garden.SatelliteFile = file;
            _garden.SatelliteCmPerPixel = MapMath.CmPerPixel(lat, zoom);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException or IOException)
        { problem = (problem is null ? "" : problem + "\n") + Loc.F("site_image_failed", ex.Message); }

        if (_summary is not null) _garden.ClimateJson = _summary.ToJson();
        _garden = await _repo.SaveAsync(_garden);
        ShowClimate();
        LoadSatellite();
        if (problem is null) StatusMessage = Loc.T("site_fetched"); else ErrorMessage = problem;
    });

    private static void BuildImage(byte[][] tiles, double lat, double lon, int zoom, string outFile)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            for (var i = 0; i < 9; i++)
            {
                var img = BitmapFrame.Create(new MemoryStream(tiles[i]), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                dc.DrawImage(img, new Rect(i % 3 * 256, i / 3 * 256, 256, 256));
            }
        var full = new RenderTargetBitmap(768, 768, 96, 96, PixelFormats.Pbgra32);
        full.Render(visual);
        var (_, _, fx, fy) = MapMath.Tile(lat, lon, zoom);
        const int size = 640;
        var x = (int)Math.Clamp(256 + fx * 256 - size / 2.0, 0, 768 - size);
        var y = (int)Math.Clamp(256 + fy * 256 - size / 2.0, 0, 768 - size);
        var crop = new CroppedBitmap(full, new Int32Rect(x, y, size, size));
        var enc = new JpegBitmapEncoder { QualityLevel = 88 };
        enc.Frames.Add(BitmapFrame.Create(crop));
        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
        using var fs = File.Create(outFile);
        enc.Save(fs);
    }

    private void LoadSatellite()
    {
        Satellite = SiteImage.Load(_paths, _garden);
        ScaleText = _garden.SatelliteCmPerPixel is { } cm && Satellite is not null
            ? Loc.F("site_scale", (cm * 640 / 100).ToString("0", Loc.Instance.Culture)) : "";
    }

    private void ShowClimate()
    {
        Months.Clear();
        HasClimate = _summary is not null;
        CanApplyFrost = _summary?.LastSpringFrost is not null || _summary?.FirstAutumnFrost is not null;
        if (_summary is null) { FrostText = ""; return; }
        var c = Loc.Instance.Culture;
        foreach (var m in _summary.Months)
            Months.Add(new ClimateRow(new DateTime(2001, m.Month, 1).ToString("MMMM", c), $"{m.AvgMin:0.#} °C", $"{m.AvgMax:0.#} °C", $"{m.AvgMean:0.#} °C", $"{m.Gdd10:0}", $"{m.FrostDays:0.#}"));
        FrostText = Loc.F("site_frost_summary", _summary.YearsUsed,
            _summary.LastSpringFrost?.ToString("d MMM", c) ?? "–", _summary.FirstAutumnFrost?.ToString("d MMM", c) ?? "–",
            _summary.FrostFreeDays, _summary.AbsoluteMin.ToString("0.#", c), _summary.Source);
    }

    [RelayCommand]
    private Task ApplyFrostAsync() => RunAsync(async () =>
    {
        if (_summary is null) return;
        if (_summary.LastSpringFrost is { } a) _garden.LastSpringFrost = a;
        if (_summary.FirstAutumnFrost is { } b) _garden.FirstAutumnFrost = b;
        _garden = await _repo.SaveAsync(_garden);
        StatusMessage = Loc.T("site_frost_applied");
    }, busy: false);
}

/// <summary>Loads the stored satellite picture, if any.</summary>
public static class SiteImage
{
    public static ImageSource? Load(AppPaths paths, Garden garden)
    {
        if (garden.SatelliteFile is null) return null;
        var file = Path.Combine(paths.SiteDir, garden.SatelliteFile);
        if (!File.Exists(file)) return null;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bmp.UriSource = new Uri(file);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
