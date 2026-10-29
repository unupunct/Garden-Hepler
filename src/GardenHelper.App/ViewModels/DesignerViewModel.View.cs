using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Greenhouse;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

// Remembered view settings, the garden as it was on a past date, and sensor dots on the plan.
public sealed partial class DesignerViewModel
{
    // ---- remembered between sessions ----
    private bool _prefsLoaded, _prefsLoading;
    private const string PrefOpacity = "ui.designer.backdrop_opacity", PrefBackdrop = "ui.designer.backdrop", PrefSnap = "ui.designer.snap", PrefLeft = "ui.designer.left_open";

    private async Task LoadPrefsAsync()
    {
        if (_prefsLoaded) return;
        _prefsLoading = true;
        try
        {
            if (double.TryParse(await _settings.GetAsync(PrefOpacity), NumberStyles.Float, CultureInfo.InvariantCulture, out var o)) BackdropOpacity = Math.Clamp(o, 0.1, 1);
            ShowBackdrop = await _settings.GetAsync(PrefBackdrop) != "0";
            Snap = await _settings.GetAsync(PrefSnap) != "0";
            LeftOpen = await _settings.GetAsync(PrefLeft) == "1";
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not load designer settings"); }
        finally { _prefsLoading = false; _prefsLoaded = true; }
    }

    private void SavePref(string key, string value) { if (_prefsLoaded && !_prefsLoading) _ = _settings.SetAsync(key, value); }
    partial void OnShowBackdropChanged(bool value) => SavePref(PrefBackdrop, value ? "1" : "0");
    partial void OnSnapChanged(bool value) => SavePref(PrefSnap, value ? "1" : "0");
    partial void OnLeftOpenChanged(bool value) => SavePref(PrefLeft, value ? "1" : "0");

    // ---- the garden on a past date ----
    [ObservableProperty] private DateTime? _viewDate;
    public string ViewDateInfo => ViewDate is { } d ? Loc.F("season_info", d.ToString("d MMM yyyy", Loc.Instance.Culture)) : "";
    public bool IsHistorical => ViewDate is not null;

    partial void OnViewDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(ViewDateInfo)); OnPropertyChanged(nameof(IsHistorical));
        foreach (var b in Beds) RebuildMarkers(b);
    }

    [RelayCommand] private void BackToNow() => ViewDate = null;

    /// <summary>Plantings to show: today's active ones, or, for a past date, those planted by then that were still in the ground (finished crops fade out after their usual harvest time).</summary>
    private IEnumerable<BedPlanting> VisiblePlantings(GardenBed b)
    {
        if (ViewDate is not { } vd) return b.Plantings.Where(p => p.Status != PlantingStatus.Finished);
        var date = DateOnly.FromDateTime(vd);
        return b.Plantings.Where(p =>
        {
            if (p.PlantedDate is not { } pd || pd > date) return false;
            if (p.Status != PlantingStatus.Finished) return true;
            return date <= pd.AddDays((_kb.Get(p.PlantId)?.DaysToMaturity?.Max ?? 120) + 45);
        });
    }

    // ---- sensors ----
    public ObservableCollection<PlaceOption> SensorOptions { get; } = new();
    [ObservableProperty] private PlaceOption? _selectedSensor;
    private bool _sensorSync;
    private List<PropertyFeature> _features = new();

    private void SyncSensorOption()
    {
        _sensorSync = true;
        SelectedSensor = SensorOptions.FirstOrDefault(o => o.Id == Selected?.Bed.SensorFeatureId?.ToString()) ?? SensorOptions.FirstOrDefault();
        _sensorSync = false;
    }

    partial void OnSelectedSensorChanged(PlaceOption? value)
    {
        if (_sensorSync || Selected is not { } b || value is null) return;
        b.Bed.SensorFeatureId = int.TryParse(value.Id, out var id) ? id : null;
        _ = SaveSensorAsync(b);
    }

    private async Task SaveSensorAsync(BedItem b) { await _repo.SaveAsync(b.Bed); await LoadSensorDotsAsync(); }

    /// <summary>A small dot on each bed that has a sensor source: green when the soil is fine, red when dry, blue when wet.</summary>
    private async Task LoadSensorDotsAsync()
    {
        SensorOptions.Clear(); SensorOptions.Add(new PlaceOption(null, Loc.T("sensor_none")));
        foreach (var f in _features.OrderBy(f => f.Name)) SensorOptions.Add(new PlaceOption(f.Id.ToString(), f.Name));
        var cache = new Dictionary<int, Dictionary<string, (double Value, DateTime AtUtc)>>();
        foreach (var b in Beds)
        {
            b.SensorBrush = null; b.SensorTip = "";
            if (b.Bed.SensorFeatureId is not { } id) continue;
            if (!cache.TryGetValue(id, out var latest)) cache[id] = latest = await _greenhouse.LatestAsync(id);
            var culture = Loc.Instance.Culture;
            if (latest.TryGetValue("soil_moisture", out var m))
            {
                var (hex, key) = m.Value < 25 ? ("#FFD6453A", "sensor_dry") : m.Value > 80 ? ("#FF2F7FB5", "sensor_wet") : ("#FF4C9A3E", "sensor_ok");
                b.SensorBrush = Frozen(hex); b.SensorTip = Loc.F("sensor_moisture", m.Value.ToString("0", culture), Loc.T(key));
            }
            else if (latest.TryGetValue("air_temp", out var t)) { b.SensorBrush = Frozen("#FF7C8E9B"); b.SensorTip = Loc.F("sensor_temp", t.Value.ToString("0.#", culture)); }
        }
        SyncSensorOption();
    }

    private static Brush Frozen(string hex) { var br = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); br.Freeze(); return br; }
}
