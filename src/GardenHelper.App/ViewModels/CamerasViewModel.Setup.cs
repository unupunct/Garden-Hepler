using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Cameras;

namespace GardenHelper.App.ViewModels;

public sealed record BrandOption(string Key, string Label) { public override string ToString() => Label; }
public sealed record ScanRow(string Host, string PortsText, string Label, string? Preset, bool Likely);
public sealed record EventRow(CameraEvent Event, string Time, string Kind, string Score, ImageSource? Thumb);

/// <summary>Quick setup, QR codes, network scan, movement settings, turning the camera and the recordings list.</summary>
public sealed partial class CamerasViewModel
{
    public IReadOnlyList<BrandOption> Brands { get; } = CameraPresets.All.Select(p => new BrandOption(p.Key, Loc.Enum("cam_brand", p.Key))).ToList();
    public ObservableCollection<ScanRow> ScanHits { get; } = new();
    public ObservableCollection<EventRow> Events { get; } = new();

    [ObservableProperty] private BrandOption? _setupBrand;
    [ObservableProperty] private string _setupHost = "";
    [ObservableProperty] private string _setupPort = "";
    [ObservableProperty] private string _editChannel = "1";
    [ObservableProperty] private string? _editBrand;
    [ObservableProperty] private bool _editMotion;
    [ObservableProperty] private double _editSensitivity = 50;
    [ObservableProperty] private string _editSnapEvery = "";
    [ObservableProperty] private string _editKeepDays = "14";
    [ObservableProperty] private bool _canMove;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _scanText = "";
    [ObservableProperty] private EventRow? _selectedEvent;
    [ObservableProperty] private ImageSource? _eventImage;

    partial void OnEditBrandChanged(string? value) => CanMove = CameraPresets.Get(value) is { Ptz: true };
    partial void OnEditSensitivityChanged(double value) => EditSensitivity = Math.Round(value);

    private void LoadExtras(Camera c)
    {
        EditBrand = c.Brand; SetupBrand = Brands.FirstOrDefault(b => b.Key == c.Brand) ?? Brands[0]; EditChannel = c.Channel.ToString(CultureInfo.InvariantCulture);
        EditMotion = c.MotionEnabled; EditSensitivity = c.MotionSensitivity; EditSnapEvery = c.SnapshotEveryMinutes?.ToString(CultureInfo.InvariantCulture) ?? ""; EditKeepDays = c.KeepDays.ToString(CultureInfo.InvariantCulture);
        ScanHits.Clear(); ScanText = ""; EventImage = null; SelectedEvent = null;
        _ = RefreshEventsAsync();
    }

    /// <summary>Reads the movement fields of the form into a camera. Returns an error text when a number is not usable.</summary>
    private string? ReadExtras(Camera c)
    {
        if (!int.TryParse(EditChannel.Trim(), out var ch) || ch is < 1 or > 64) ch = 1;
        int? every = null;
        if (!string.IsNullOrWhiteSpace(EditSnapEvery)) { if (!int.TryParse(EditSnapEvery.Trim(), out var m) || m is < 1 or > 1440) return Loc.T("cam_bad_motion"); every = m; }
        if (!int.TryParse(EditKeepDays.Trim(), out var days) || days is < 1 or > 365) return Loc.T("cam_bad_motion");
        c.Brand = EditBrand; c.Channel = ch; c.MotionEnabled = EditMotion; c.MotionSensitivity = (int)Math.Clamp(EditSensitivity, 1, 100); c.SnapshotEveryMinutes = every; c.KeepDays = days;
        return null;
    }

    // ---------- quick setup ----------
    [RelayCommand]
    private void ApplySetup()
    {
        var preset = CameraPresets.Get(SetupBrand?.Key ?? "generic")!;
        int? port = null;
        if (!string.IsNullOrWhiteSpace(SetupPort)) { if (!int.TryParse(SetupPort.Trim(), out var p) || p is < 1 or > 65535) { ErrorMessage = Loc.T("cam_setup_bad_host"); return; } port = p; }
        _ = int.TryParse(EditChannel.Trim(), out var ch);
        if (CameraPresets.BuildUrl(preset, SetupHost, port, ch < 1 ? 1 : ch) is not { } url) { ErrorMessage = Loc.T("cam_setup_bad_host"); return; }
        EditBrand = preset.Key; EditKind = Kinds.First(k => k.Key == preset.Kind); EditUrl = url;
        ErrorMessage = null; StatusMessage = Loc.T("cam_setup_done");
    }

    [RelayCommand]
    private Task FindCamerasAsync() => RunAsync(async () =>
    {
        if (IsScanning || !_dialogs.Confirm(Loc.T("cam_find_confirm"))) return;
        IsScanning = true; ScanHits.Clear();
        try
        {
            var hosts = ConnectViewModel.LocalAddresses().SelectMany(CameraScanner.Subnet24Hosts).Distinct().ToList();
            var total = hosts.Count;
            var progress = new Progress<int>(n => ScanText = Loc.F("cam_finding", n, total));
            var found = await CameraScanner.ScanAsync(hosts, null, _http, progress);
            foreach (var r in found)
                ScanHits.Add(new ScanRow(r.Host, string.Join(", ", r.OpenPorts), r.Likely ? Loc.T("cam_likely") : Loc.T("cam_other_device"), r.PresetGuess, r.Likely));
            ScanText = found.Count == 0 ? Loc.T("cam_none_found") : Loc.F("cam_found", found.Count);
        }
        finally { IsScanning = false; }
    }, busy: false);

    [RelayCommand]
    private void UseScanHit(ScanRow row)
    {
        SetupHost = row.Host;
        SetupBrand = Brands.FirstOrDefault(b => b.Key == row.Preset) ?? Brands[0];
        var ports = row.PortsText.Split(", ");
        SetupPort = ports.Contains("80") || !ports.Contains("8080") ? "" : "8080";
        StatusMessage = Loc.T("cam_setup_done_scan");
    }

    // ---------- QR code ----------
    [RelayCommand]
    private void ReadQrFile()
    {
        if (_dialogs.PickFile(Loc.T("cam_qr_filter")) is not { } file) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(file); bmp.EndInit(); bmp.Freeze();
            FillFromQr(QrCodes.Decode(bmp));
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException) { ErrorMessage = Loc.T("cam_qr_none"); }
    }

    [RelayCommand]
    private void ReadQrClipboard()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsImage() && System.Windows.Clipboard.GetImage() is { } img) FillFromQr(QrCodes.Decode(img));
            else if (System.Windows.Clipboard.ContainsText()) FillFromQr(System.Windows.Clipboard.GetText());
            else ErrorMessage = Loc.T("cam_qr_none");
        }
        catch (System.Runtime.InteropServices.ExternalException) { ErrorMessage = Loc.T("cam_qr_none"); }
    }

    private void FillFromQr(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) { ErrorMessage = Loc.T("cam_qr_none"); return; }
        if (CameraQrParser.Parse(text) is not { } r) { ErrorMessage = Loc.T("cam_qr_not_camera"); return; }
        EditKind = Kinds.First(k => k.Key == r.Kind); EditUrl = r.Url;
        if (r.Username is not null) EditUser = r.Username;
        if (r.Password is not null) EditPassword = r.Password;
        ErrorMessage = null; StatusMessage = Loc.T("cam_qr_ok");
    }

    // ---------- turning the camera ----------
    [RelayCommand]
    private Task MoveAsync(string direction) => RunAsync(async () =>
    {
        var (cam, problem) = ReadForm();
        if (cam is null) { if (problem is not null) ErrorMessage = problem; return; }
        cam.Brand = EditBrand; _ = int.TryParse(EditChannel.Trim(), out var ch); cam.Channel = ch < 1 ? 1 : ch;
        var pw = string.IsNullOrEmpty(EditPassword) ? CameraSecrets.Unprotect(cam.SecretProtected) : EditPassword;
        try
        {
            await _client.MoveAsync(cam, direction, pw);
            if (direction != "stop") { await Task.Delay(350); await _client.MoveAsync(cam, "stop", pw); }       // a press nudges; it does not run away
            await Task.Delay(500); await GrabNowAsync();
        }
        catch (CameraException ex) { ErrorMessage = Loc.F("cam_move_fail", ex.Message); }
    }, busy: false);

    private Task GrabNowAsync() => TestCommand.ExecuteAsync(null);

    // ---------- recordings ----------
    [RelayCommand]
    private async Task RefreshEventsAsync()
    {
        if (Selected is not { } cam) { Events.Clear(); return; }
        var list = await _events.ListAsync(cam.Id, 100);
        if (Selected?.Id != cam.Id) return;
        Events.Clear();
        foreach (var e in list)
            Events.Add(new EventRow(e, e.AtUtc.ToLocalTime().ToString("g", Loc.Instance.Culture), Loc.T("cam_ev_" + e.Kind), e.Score is { } s ? Loc.F("cam_ev_score", s.ToString("0.#", Loc.Instance.Culture)) : "",
                PhotoImage.Load(_store.PathOf(e.File), 160)));
    }

    partial void OnSelectedEventChanged(EventRow? value) => EventImage = value is null ? null : PhotoImage.Load(_store.PathOf(value.Event.File), 1100);

    [RelayCommand]
    private Task SnapshotNowAsync() => RunAsync(async () =>
    {
        var (cam, problem) = ReadForm();
        if (cam is null) { if (problem is not null) ErrorMessage = problem; return; }
        try
        {
            var bytes = await _client.GrabFrameAsync(cam, string.IsNullOrEmpty(EditPassword) ? CameraSecrets.Unprotect(cam.SecretProtected) : EditPassword);
            await _monitor.RecordAsync(Selected!, "manual", null, bytes, DateTime.UtcNow);
            await RefreshEventsAsync();
            StatusMessage = Loc.T("cam_ev_taken");
        }
        catch (CameraException ex) { ErrorMessage = ex.Message; }
    }, busy: false);

    [RelayCommand]
    private Task DeleteEventAsync(EventRow row) => RunAsync(async () =>
    {
        await _events.DeleteAsync(row.Event);
        if (SelectedEvent == row) SelectedEvent = null;
        await RefreshEventsAsync();
    }, busy: false);

    [RelayCommand] private void OpenRecordings() => _dialogs.OpenFolder(_store.Root);

    private void OnMonitorEvent(CameraEvent e) =>
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => { if (Selected?.Id == e.CameraId) _ = RefreshEventsAsync(); }));
}
