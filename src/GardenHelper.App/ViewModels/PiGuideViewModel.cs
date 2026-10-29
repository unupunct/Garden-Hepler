using System.IO;
using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Services;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

/// <summary>The Raspberry Pi first-time guide shown inside the app, with this PC's address, the secret token and the place number filled in.</summary>
public sealed partial class PiGuideViewModel : PageViewModel
{
    private readonly LocalApiServer _api;
    private readonly SettingsService _settings;
    private readonly INavigator _nav;
    private readonly IDialogs _dialogs;

    public PiGuideViewModel(LocalApiServer api, SettingsService settings, INavigator nav, IDialogs dialogs, ILogger<PiGuideViewModel> log) : base(log)
    { _api = api; _settings = settings; _nav = nav; _dialogs = dialogs; }

    /// <summary>The greenhouse (or place) the commands in the guide are for. Set by the page that opens the guide.</summary>
    public int HouseId { get; set; }
    [ObservableProperty] private FlowDocument? _document;
    [ObservableProperty] private string _pcAddress = "";

    private static string? FindFolder(string relative)
    {
        var inApp = Path.Combine(AppContext.BaseDirectory, relative);
        if (Directory.Exists(inApp)) return inApp;
        var dev = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relative));
        return Directory.Exists(dev) ? dev : null;
    }

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var dir = FindFolder("docs");
        var file = dir is null ? null : Path.Combine(dir, "RASPBERRY-PI-SETUP.md");
        if (file is null || !File.Exists(file)) { ErrorMessage = Loc.T("gh_guide_missing"); return; }
        var port = await _settings.GetAsync(LocalApiServer.PortKey) ?? LocalApiServer.DefaultPort.ToString();
        var token = await _api.TokenAsync();
        PcAddress = GreenhouseViewModel.LocalAddress() ?? "192.168.1.20";
        var md = await File.ReadAllTextAsync(file);
        // make the examples ready to copy: this PC, this port, this token, this place
        md = md.Replace("192.168.1.20:8765", $"{PcAddress}:{port}").Replace("YOUR_TOKEN", token).Replace("--greenhouse 8", $"--greenhouse {(HouseId > 0 ? HouseId : 1)}");
        var intro = $"> **Your values, already filled in below:** this PC `{PcAddress}`, port `{port}`, place number `{(HouseId > 0 ? HouseId : 1)}`. The secret token is in the commands: do not share them.\n\n";
        var at = md.IndexOf('\n', md.IndexOf("# ", StringComparison.Ordinal)) + 1;
        Document = MarkdownLite.Build(md[..at] + "\n" + intro + md[at..]);
    });

    [RelayCommand] private void Back() => _nav.GoTo<GreenhouseViewModel>();
    [RelayCommand] private void OpenFolder() { if (FindFolder(Path.Combine("tools", "raspberry-pi")) is { } d) _dialogs.OpenFolder(d); else ErrorMessage = Loc.T("gh_guide_missing"); }
}
