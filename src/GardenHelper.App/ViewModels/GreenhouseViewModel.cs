using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Greenhouse;
using GardenHelper.Core.Services;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record ReadingTile(string Label, string Value, string Age, bool Stale);
public sealed record ActuatorRow(string Key, string Label, string State, string Mode, bool IsOn, bool IsAuto);
public sealed record AlertLine(string Glyph, string Text, string Severity);

/// <summary>One thing inside the greenhouse layout, movable by dragging.</summary>
public sealed partial class LayoutItem : ObservableObject
{
    public LayoutItem(GreenhouseItem item) { Item = item; }
    public GreenhouseItem Item { get; }
    public int Id => Item.Id;
    public double X { get => Item.X; set { Item.X = value; OnPropertyChanged(); } }
    public double Y { get => Item.Y; set { Item.Y = value; OnPropertyChanged(); } }
    public double Width { get => Item.Width; set { Item.Width = Math.Max(10, value); OnPropertyChanged(); } }
    public double Height { get => Item.Height; set { Item.Height = Math.Max(10, value); OnPropertyChanged(); } }
    public string Name { get => Item.Name; set { Item.Name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Label)); } }
    public string Label => string.IsNullOrWhiteSpace(Item.Name) ? Loc.T("gh_item_" + Item.Kind) : Item.Name;
    [ObservableProperty] private bool _isSelected;
    public Brush Fill => Item.Kind switch
    {
        "growbed" => Frozen(0xFF, 0xD3, 0xEA, 0xC2), "pots" => Frozen(0xFF, 0xE4, 0xCF, 0xB4), "seedtray" => Frozen(0xFF, 0xCF, 0xE6, 0xA8), "shelf" => Frozen(0xFF, 0xE6, 0xD6, 0xB4),
        "channel" => Frozen(0xFF, 0xBD, 0xE0, 0xF0), "tank" => Frozen(0xFF, 0x9F, 0xCF, 0xEA), "sink" => Frozen(0xFF, 0xC9, 0xE4, 0xEE), "sensor" => Frozen(0xFF, 0xFF, 0xE3, 0x8A),
        "light" => Frozen(0xFF, 0xFF, 0xF1, 0xA8), "fan" => Frozen(0xFF, 0xD8, 0xE2, 0xEA), "heater" => Frozen(0xFF, 0xF6, 0xC5, 0xB4), "mister" => Frozen(0xFF, 0xCF, 0xEA, 0xF2),
        "vent" => Frozen(0xFF, 0xDD, 0xE3, 0xE8), "door" => Frozen(0xFF, 0xD9, 0xB9, 0x95), "pump" => Frozen(0xFF, 0xD9, 0xCF, 0xEE), "path" => Frozen(0xFF, 0xEC, 0xE4, 0xD0),
        "other" => Frozen(0xFF, 0xE4, 0xDD, 0xD2), _ => Frozen(0xFF, 0xEB, 0xD7, 0xAE),
    };
    public Brush Edge => Item.Kind switch
    {
        "growbed" or "seedtray" => Frozen(0xFF, 0x7F, 0xA6, 0x66), "channel" or "tank" or "sink" or "mister" => Frozen(0xFF, 0x5F, 0x9F, 0xC4), "sensor" or "light" => Frozen(0xFF, 0xC9, 0xA2, 0x1F),
        "door" => Frozen(0xFF, 0x9C, 0x6F, 0x3F), "heater" => Frozen(0xFF, 0xC9, 0x78, 0x5F), _ => Frozen(0xFF, 0xA8, 0x96, 0x78),
    };
    public string Glyph => Item.Kind switch
    {
        "growbed" => "❧", "pots" => "❀", "seedtray" => "⁘", "shelf" => "≡", "channel" => "≈", "tank" => "◍", "sink" => "☂", "sensor" => "◉",
        "light" => "☀", "fan" => "✻", "heater" => "♨", "mister" => "∴", "vent" => "⬆", "door" => "◠", "pump" => "↻", "path" => "┅", "bench" => "▭", _ => "•",
    };
    private static Brush Frozen(byte a, byte r, byte g, byte b) { var br = new SolidColorBrush(Color.FromArgb(a, r, g, b)); br.Freeze(); return br; }
}

/// <summary>Greenhouse control panel: live sensor values from the Raspberry Pi, vents/water/fans, limits, alerts, the layout inside, and the Pi connection.</summary>
public sealed partial class GreenhouseViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly GreenhouseService _svc;
    private readonly LocalApiServer _api;
    private readonly SettingsService _settings;
    private readonly IDialogs _dialogs;
    private readonly INavigator _nav;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private Garden? _garden;
    private GreenhouseSetup? _setup;
    private bool _loading;
    private readonly ILogger _logger;

    public GreenhouseViewModel(Repository repo, GreenhouseService svc, LocalApiServer api, SettingsService settings, IDialogs dialogs, INavigator nav, ILogger<GreenhouseViewModel> log) : base(log)
    {
        _nav = nav; _logger = log; _repo = repo; _svc = svc; _api = api; _settings = settings; _dialogs = dialogs;
        Systems = GreenhouseCatalog.Systems.Select(k => new AnimalOption(k, Loc.T("gh_sys_" + k))).ToList();
        ItemKinds = GreenhouseCatalog.ItemKinds.Select(k => new AnimalOption(k, Loc.T("gh_item_" + k))).ToList();
        _newKind = ItemKinds[0];
        _timer.Tick += async (_, _) => await RefreshLiveAsync();
    }

    public IReadOnlyList<AnimalOption> Systems { get; }
    public IReadOnlyList<AnimalOption> ItemKinds { get; }
    public ObservableCollection<PlaceOption> Houses { get; } = new();
    public ObservableCollection<ReadingTile> Tiles { get; } = new();
    public ObservableCollection<ActuatorRow> Actuators { get; } = new();
    public ObservableCollection<AlertLine> Alerts { get; } = new();
    public ObservableCollection<LayoutItem> Items { get; } = new();

    [ObservableProperty] private PlaceOption? _house;
    [ObservableProperty] private bool _hasHouse;
    [ObservableProperty] private string _noHouseText = "";
    [ObservableProperty] private AnimalOption? _growSystem;
    [ObservableProperty] private bool _isWater;
    [ObservableProperty] private bool _autoControl = true;
    [ObservableProperty] private string _tempMax = "", _tempMin = "", _humidityMax = "", _soilMin = "", _waterTempMax = "", _phMin = "", _phMax = "", _widthCm = "", _lengthCm = "";
    [ObservableProperty] private double _canvasWidth = 600;
    [ObservableProperty] private double _canvasHeight = 300;
    [ObservableProperty] private double _labelSize = 14;
    [ObservableProperty] private LayoutItem? _selectedItem;
    [ObservableProperty] private bool _hasItem;
    [ObservableProperty] private string _itemName = "", _itemWidth = "", _itemHeight = "", _itemNotes = "";
    [ObservableProperty] private AnimalOption _newKind;
    [ObservableProperty] private bool _apiEnabled;
    [ObservableProperty] private string _apiPort = LocalApiServer.DefaultPort.ToString();
    [ObservableProperty] private string _apiToken = "";
    [ObservableProperty] private string _apiStatus = "";
    [ObservableProperty] private string _piExample = "", _piCheckExample = "", _piRunExample = "", _pcAddress = "";

    private static double? Num(string s) => double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            _garden = await _repo.ActiveGardenAsync();
            Houses.Clear();
            if (_garden is not null)
                foreach (var f in await _repo.ListAsync<PropertyFeature>(q => q.Where(x => x.GardenId == _garden.Id && x.Kind == "greenhouse").OrderBy(x => x.Name))) Houses.Add(new PlaceOption(f.Id.ToString(), f.Name));
            HasHouse = Houses.Count > 0;
            NoHouseText = Loc.T("gh_none");
            House = Houses.FirstOrDefault(h => h.Id == House?.Id) ?? Houses.FirstOrDefault();
            ApiEnabled = await _settings.GetAsync(LocalApiServer.EnabledKey) == "1";
            ApiPort = await _settings.GetAsync(LocalApiServer.PortKey) ?? LocalApiServer.DefaultPort.ToString();
            ApiToken = await _api.TokenAsync();
            UpdateApiTexts();
        });
        _timer.Start();
    }

    public void Stop() => _timer.Stop();
    public void Resume() { if (HasHouse) _timer.Start(); }

    private int HouseId => int.TryParse(House?.Id, out var id) ? id : 0;

    partial void OnHouseChanged(PlaceOption? value) { if (value is not null) _ = LoadHouseAsync(); }

    private async Task LoadHouseAsync()
    {
        if (HouseId == 0) return;
        _loading = true;
        try
        {
            _setup = await _svc.SetupAsync(HouseId);
            GrowSystem = Systems.First(s => s.Key == _setup.System); IsWater = _setup.System != "soil"; AutoControl = _setup.AutoControl;
            (TempMax, TempMin, HumidityMax, SoilMin) = (F(_setup.TempMax), F(_setup.TempMin), F(_setup.HumidityMax), F(_setup.SoilMoistureMin));
            (WaterTempMax, PhMin, PhMax, WidthCm, LengthCm) = (F(_setup.WaterTempMax), F(_setup.PhMin), F(_setup.PhMax), F(_setup.WidthCm), F(_setup.LengthCm));
            CanvasWidth = _setup.WidthCm; CanvasHeight = _setup.LengthCm; LabelSize = Math.Max(8, _setup.WidthCm / 45);
            Items.Clear();
            foreach (var i in await _repo.ListAsync<GreenhouseItem>(q => q.Where(x => x.FeatureId == HouseId).OrderBy(x => x.Id))) Items.Add(new LayoutItem(i));
            SelectedItem = null;
        }
        finally { _loading = false; }
        await RefreshLiveAsync();
    }

    partial void OnGrowSystemChanged(AnimalOption? value)
    {
        IsWater = value is not null && value.Key != "soil";
        if (_loading || value is null || _setup is null) return;
        var tmp = new GreenhouseSetup { System = value.Key }; GreenhouseCatalog.ApplyDefaults(tmp);
        (PhMin, PhMax, WaterTempMax) = (F(tmp.PhMin), F(tmp.PhMax), F(tmp.WaterTempMax));
    }

    /// <summary>Reloads the live values, switches and alerts (every few seconds while the page is open).</summary>
    public async Task RefreshLiveAsync()
    {
        if (HouseId == 0 || _setup is null) return;
        try
        {
            var latest = await _svc.LatestAsync(HouseId);
            var now = DateTime.UtcNow;
            Tiles.Clear();
            foreach (var m in GreenhouseCatalog.Metrics)
            {
                if (!latest.TryGetValue(m.Key, out var r)) continue;
                var age = (now - r.AtUtc).TotalMinutes;
                Tiles.Add(new ReadingTile(Loc.T("gh_m_" + m.Key), $"{r.Value.ToString(m.Key == "light" ? "0" : "0.#", Loc.Instance.Culture)} {m.Unit}",
                    age < 1 ? Loc.T("gh_just_now") : Loc.F("gh_min_ago", Math.Round(age)), age > 15));
            }
            Actuators.Clear();
            var acts = (await _svc.ActuatorsAsync(HouseId)).ToDictionary(a => a.Key);
            foreach (var k in GreenhouseCatalog.ActuatorKeys)
            {
                acts.TryGetValue(k, out var a);
                Actuators.Add(new ActuatorRow(k, Loc.T("gh_act_" + k), Loc.T(a?.Desired == true ? "gh_on" : "gh_off"), Loc.T(a is null || a.Auto ? "gh_auto" : "gh_manual"), a?.Desired == true, a is null || a.Auto));
            }
            var values = latest.ToDictionary(kv => kv.Key, kv => kv.Value.Value);
            DateTime? last = latest.Count == 0 ? null : latest.Values.Max(v => v.AtUtc);
            Alerts.Clear();
            foreach (var al in GreenhouseRules.Evaluate(_setup, values, last, now))
                Alerts.Add(new AlertLine(al.Severity == "critical" ? "⛔" : al.Severity == "warning" ? "⚠" : "ℹ", Loc.F(al.Key, al.Args), al.Severity));
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Greenhouse refresh failed"); }
    }

    [RelayCommand]
    private Task CreateHouseAsync() => RunAsync(async () =>
    {
        _garden ??= await _repo.SaveAsync(new Garden { Name = Loc.T("site_default_name") });
        var f = await _repo.SaveAsync(new PropertyFeature { GardenId = _garden.Id, Name = Loc.T("gh_default_name"), Kind = "greenhouse" });
        Houses.Add(new PlaceOption(f.Id.ToString(), f.Name)); HasHouse = true; House = Houses.Last();
    }, busy: false);

    [RelayCommand]
    private Task SaveSettingsAsync() => RunAsync(async () =>
    {
        if (_setup is null) return;
        var v = new[] { Num(TempMax), Num(TempMin), Num(HumidityMax), Num(SoilMin), Num(WaterTempMax), Num(PhMin), Num(PhMax), Num(WidthCm), Num(LengthCm) };
        if (v.Any(x => x is null) || v[0] <= v[1] || v[2] is < 1 or > 100 || v[3] is < 0 or > 100 || v[5] >= v[6] || v[5] < 0 || v[6] > 14 || v[7] is < 100 or > 5000 || v[8] is < 100 or > 5000)
        { ErrorMessage = Loc.T("gh_bad_settings"); return; }
        (_setup.TempMax, _setup.TempMin, _setup.HumidityMax, _setup.SoilMoistureMin) = (v[0]!.Value, v[1]!.Value, v[2]!.Value, v[3]!.Value);
        (_setup.WaterTempMax, _setup.PhMin, _setup.PhMax, _setup.WidthCm, _setup.LengthCm) = (v[4]!.Value, v[5]!.Value, v[6]!.Value, v[7]!.Value, v[8]!.Value);
        _setup.System = GrowSystem?.Key ?? "soil"; _setup.AutoControl = AutoControl;
        await _repo.SaveAsync(_setup);
        CanvasWidth = _setup.WidthCm; CanvasHeight = _setup.LengthCm; LabelSize = Math.Max(8, _setup.WidthCm / 45);
        await _svc.ApplyRulesAsync(HouseId);
        await RefreshLiveAsync();
        StatusMessage = Loc.T("saved");
    }, busy: false);

    [RelayCommand]
    private Task SwitchAsync(ActuatorRow row) => RunAsync(async () => { await _svc.SetActuatorAsync(HouseId, row.Key, !row.IsOn, auto: false); await RefreshLiveAsync(); }, busy: false);

    [RelayCommand]
    private Task AutoAsync(ActuatorRow row) => RunAsync(async () =>
    {
        await _svc.SetActuatorAsync(HouseId, row.Key, row.IsOn, auto: true);
        await _svc.ApplyRulesAsync(HouseId); await RefreshLiveAsync();
    }, busy: false);

    [RelayCommand] private void OpenPiGuide() { var id = HouseId; _nav.GoTo<PiGuideViewModel>(vm => vm.HouseId = id); }
    [RelayCommand] private void OpenLayout() { var id = HouseId; _nav.GoTo<InteriorViewModel>(vm => vm.FeatureId = id); }

    // ---- layout inside the greenhouse ----
    partial void OnSelectedItemChanged(LayoutItem? old, LayoutItem? value)
    {
        if (old is not null) old.IsSelected = false;
        HasItem = value is not null;
        if (value is null) return;
        value.IsSelected = true; ItemName = value.Item.Name; ItemWidth = F(value.Width); ItemHeight = F(value.Height); ItemNotes = value.Item.Notes ?? "";
    }

    [RelayCommand]
    private Task AddItemAsync() => RunAsync(async () =>
    {
        if (HouseId == 0) return;
        var (w, h) = NewKind.Key switch { "bench" => (120.0, 50.0), "growbed" => (120.0, 60.0), "pots" => (90.0, 40.0), "seedtray" => (60.0, 40.0), "shelf" => (100.0, 30.0), "channel" => (200.0, 20.0), "tank" => (80.0, 80.0), "sink" => (60.0, 45.0), "door" => (90.0, 12.0), "path" => (60.0, 200.0), "vent" => (80.0, 14.0), "light" => (80.0, 14.0), _ => (30.0, 30.0) };
        var item = await _repo.SaveAsync(new GreenhouseItem { FeatureId = HouseId, Kind = NewKind.Key, X = 20, Y = 20, Width = w, Height = h });
        var li = new LayoutItem(item); Items.Add(li); SelectedItem = li;
    }, busy: false);

    [RelayCommand]
    private Task SaveItemAsync() => RunAsync(async () =>
    {
        if (SelectedItem is not { } li) return;
        if (Num(ItemWidth) is not { } w || Num(ItemHeight) is not { } h || w is < 10 or > 5000 || h is < 10 or > 5000) { ErrorMessage = Loc.T("gh_bad_item"); return; }
        li.Name = ItemName.Trim(); li.Width = w; li.Height = h; li.Item.Notes = string.IsNullOrWhiteSpace(ItemNotes) ? null : ItemNotes;
        await _repo.SaveAsync(li.Item);
        StatusMessage = Loc.T("saved");
    }, busy: false);

    [RelayCommand]
    private Task DeleteItemAsync() => RunAsync(async () =>
    {
        if (SelectedItem is not { } li || !_dialogs.Confirm(Loc.F("gh_delete_item", li.Label))) return;
        await _repo.DeleteAsync(li.Item); Items.Remove(li); SelectedItem = null;
    }, busy: false);

    /// <summary>Called when a drag ends: keeps the item inside the greenhouse and saves where it is now.</summary>
    public async Task ItemMovedAsync(LayoutItem li)
    {
        li.X = Math.Clamp(li.X, 0, Math.Max(0, CanvasWidth - li.Width)); li.Y = Math.Clamp(li.Y, 0, Math.Max(0, CanvasHeight - li.Height));
        await _repo.SaveAsync(li.Item);
    }

    // ---- Raspberry Pi connection ----
    /// <summary>This PC's address on the home network (a private IPv4 address), so the Pi command can be copied as it is.</summary>
    public static string? LocalAddress()
    {
        try
        {
            var found = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && n.NetworkInterfaceType is not (System.Net.NetworkInformation.NetworkInterfaceType.Loopback or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel))
                .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(a => a.ToString())
                .Where(a => a.StartsWith("192.168.") || a.StartsWith("10.") || (a.StartsWith("172.") && int.TryParse(a.Split('.')[1], out var b) && b is >= 16 and <= 31)).ToList();
            return found.FirstOrDefault(a => a.StartsWith("192.168.")) ?? found.FirstOrDefault();
        }
        catch { return null; }
    }

    private void UpdateApiTexts()
    {
        ApiStatus = _api.Running ? Loc.F("gh_api_on", _api.Port) : Loc.T("gh_api_off");
        PcAddress = LocalAddress() ?? $"<{Loc.T("gh_pc_address")}>";
        PiExample = $"python3 greenhouse_agent.py --server http://{PcAddress}:{ApiPort} --token {ApiToken} --greenhouse {HouseId}";
        PiCheckExample = PiExample + " --check";
        PiRunExample = PiExample + " --simulate";
    }

    partial void OnApiEnabledChanged(bool value) { if (!_loading) _ = ApplyApiAsync(); }

    [RelayCommand]
    private Task ApplyApiAsync() => RunAsync(async () =>
    {
        if (!int.TryParse(ApiPort, out var port) || port is < 1025 or > 65535) { ErrorMessage = Loc.T("gh_bad_port"); return; }
        await _settings.SetAsync(LocalApiServer.PortKey, port.ToString());
        await _settings.SetAsync(LocalApiServer.EnabledKey, ApiEnabled ? "1" : "0");
        try
        {
            if (_api.Running && (!ApiEnabled || _api.Port != port)) await _api.StopAsync();
            if (ApiEnabled && !_api.Running) await _api.StartAsync(port, await _api.TokenAsync());
        }
        catch (Exception ex) { ErrorMessage = Loc.F("gh_api_fail", ex.Message); ApiEnabled = false; }
        UpdateApiTexts();
    }, busy: false);

    [RelayCommand]
    private Task NewTokenAsync() => RunAsync(async () =>
    {
        if (!_dialogs.Confirm(Loc.T("gh_new_token_confirm"))) return;
        ApiToken = await _api.ResetTokenAsync(); UpdateApiTexts(); StatusMessage = Loc.T("gh_new_token_done");
    }, busy: false);

    [RelayCommand] private void CopyText(string text) { try { System.Windows.Clipboard.SetText(text); StatusMessage = Loc.T("gh_copied"); } catch { ErrorMessage = Loc.T("gh_copy_fail"); } }
    [RelayCommand] private void OpenGuideFolder()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "tools", "raspberry-pi");
        if (!System.IO.Directory.Exists(dir)) dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "raspberry-pi"));
        if (System.IO.Directory.Exists(dir)) _dialogs.OpenFolder(dir); else ErrorMessage = Loc.T("gh_guide_missing");
    }
    [RelayCommand] private void CopyExample() { try { System.Windows.Clipboard.SetText(PiExample); StatusMessage = Loc.T("gh_copied"); } catch { ErrorMessage = Loc.T("gh_copy_fail"); } }
}
