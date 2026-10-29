using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Services;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record AddressOption(string Address) { public override string ToString() => Address; }

/// <summary>Phone and Home Assistant connection: switch the local API on, see the phone link and the Home Assistant settings to paste.</summary>
public sealed partial class ConnectViewModel : PageViewModel
{
    private readonly LocalApiServer _api;
    private readonly SettingsService _settings;
    private readonly IDialogs _dialogs;
    private bool _loading;

    public ConnectViewModel(LocalApiServer api, SettingsService settings, IDialogs dialogs, ILogger<ConnectViewModel> log) : base(log)
    { _api = api; _settings = settings; _dialogs = dialogs; }

    public ObservableCollection<AddressOption> Addresses { get; } = new();
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _port = LocalApiServer.DefaultPort.ToString();
    [ObservableProperty] private string _token = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private AddressOption? _address;
    [ObservableProperty] private string _phoneLink = "";
    [ObservableProperty] private string _homeAssistant = "";

    /// <summary>Addresses of this PC on the local network (private ranges first), plus the VPN address when there is one.</summary>
    public static IEnumerable<string> LocalAddresses()
    {
        var found = new List<(string Ip, int Rank)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var b = ua.Address.GetAddressBytes();
                if (b[0] == 169 && b[1] == 254) continue;                                    // no network configured
                var rank = b[0] == 100 && b[1] is >= 64 and <= 127 ? 1                        // Tailscale / CGNAT
                    : b[0] == 192 && b[1] == 168 ? 0 : b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) ? 0 : 2;
                found.Add((ua.Address.ToString(), rank));
            }
        }
        return found.OrderBy(f => f.Rank).Select(f => f.Ip).Distinct();
    }

    public override Task LoadAsync() => RunAsync(async () =>
    {
        _loading = true;
        try
        {
            Enabled = await _settings.GetAsync(LocalApiServer.EnabledKey) == "1";
            Port = await _settings.GetAsync(LocalApiServer.PortKey) ?? LocalApiServer.DefaultPort.ToString();
            Token = await _api.TokenAsync();
            Addresses.Clear(); foreach (var ip in LocalAddresses()) Addresses.Add(new AddressOption(ip));
            if (Addresses.Count == 0) Addresses.Add(new AddressOption("127.0.0.1"));
            Address = Addresses[0];
        }
        finally { _loading = false; }
        Rebuild();
    });

    partial void OnAddressChanged(AddressOption? value) => Rebuild();
    partial void OnEnabledChanged(bool value) { if (!_loading) _ = ApplyAsync(); }

    private string Base => $"http://{Address?.Address ?? "127.0.0.1"}:{Port}";

    private void Rebuild()
    {
        Status = _api.Running ? Loc.F("gh_api_on", _api.Port) : Loc.T("gh_api_off");
        PhoneLink = $"{Base}/#t={Token}";
        HomeAssistant = BuildHomeAssistant(Base, Token);
    }

    /// <summary>Ready-to-paste Home Assistant configuration for this PC and token.</summary>
    public static string BuildHomeAssistant(string baseUrl, string token) => $$$"""
        # Garden Helper: paste into configuration.yaml, then restart Home Assistant.
        rest:
          - resource: {{{baseUrl}}}/api/v1/summary
            scan_interval: 60
            headers:
              Authorization: "Bearer {{{token}}}"
            sensor:
              - name: "Garden tasks overdue"
                value_template: "{{ value_json.tasks.overdue }}"
              - name: "Garden tasks today"
                value_template: "{{ value_json.tasks.today }}"
              - name: "Garden plants to water"
                value_template: "{{ value_json.wateringDue }}"
              - name: "Eggs today"
                value_template: "{{ value_json.eggsToday }}"
              - name: "Greenhouse air temperature"
                unit_of_measurement: "°C"
                device_class: temperature
                value_template: "{{ value_json.greenhouses[0].airTemp if value_json.greenhouses else none }}"
            binary_sensor:
              - name: "Animal treatment withdrawal"
                value_template: "{{ value_json.withdrawalActive }}"

        rest_command:
          garden_task_done:
            url: "{{{baseUrl}}}/api/v1/tasks/{{ id }}/done"
            method: post
            headers:
              Authorization: "Bearer {{{token}}}"
          greenhouse_vent:
            url: "{{{baseUrl}}}/api/v1/greenhouse/{{ house }}/switch/vent"
            method: post
            content_type: "application/json"
            payload: '{"on": {{ on }} }'
            headers:
              Authorization: "Bearer {{{token}}}"

        # Garden Helper cameras as Home Assistant cameras (camera 1 shown; the number is the camera's id).
        camera:
          - platform: generic
            name: "Garden camera 1"
            still_image_url: "{{{baseUrl}}}/api/v1/cameras/1/snapshot"
            authentication: basic
            username: ha
            password: "{{{token}}}"
        """;

    [RelayCommand]
    private Task ApplyAsync() => RunAsync(async () =>
    {
        if (!int.TryParse(Port, out var port) || port is < 1025 or > 65535) { ErrorMessage = Loc.T("gh_bad_port"); return; }
        await _settings.SetAsync(LocalApiServer.PortKey, port.ToString());
        await _settings.SetAsync(LocalApiServer.EnabledKey, Enabled ? "1" : "0");
        try
        {
            if (_api.Running && (!Enabled || _api.Port != port)) await _api.StopAsync();
            if (Enabled && !_api.Running) await _api.StartAsync(port, await _api.TokenAsync());
        }
        catch (Exception ex) { ErrorMessage = Loc.F("gh_api_fail", ex.Message); _loading = true; Enabled = false; _loading = false; }
        Rebuild();
    }, busy: false);

    [RelayCommand]
    private Task NewTokenAsync() => RunAsync(async () =>
    {
        if (!_dialogs.Confirm(Loc.T("connect_new_token_confirm"))) return;
        Token = await _api.ResetTokenAsync(); Rebuild(); StatusMessage = Loc.T("gh_new_token_done");
    }, busy: false);

    [RelayCommand] private void CopyLink() => Copy(PhoneLink);
    [RelayCommand] private void CopyHomeAssistant() => Copy(HomeAssistant);
    private void Copy(string text) { try { System.Windows.Clipboard.SetText(text); StatusMessage = Loc.T("gh_copied"); } catch { ErrorMessage = Loc.T("gh_copy_fail"); } }
}
