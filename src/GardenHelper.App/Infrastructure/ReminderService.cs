using System.Windows;
using System.Windows.Threading;
using GardenHelper.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.Infrastructure;

/// <summary>
/// Windows notifications while the app is running (it can sit minimised): urgent things as they appear (frost tonight, a plant badly overdue, a greenhouse alert)
/// and a short summary once a day at the chosen time. Everything is computed on this computer.
/// </summary>
public sealed class ReminderService : IDisposable
{
    public const string EnabledKey = "reminders.enabled", SummaryKey = "reminders.summary_time", LastSummaryKey = "reminders.last_summary";
    private readonly TodayService _today;
    private readonly SettingsService _settings;
    private readonly Repository _repo;
    private readonly IServiceProvider _services;
    private readonly ILogger<ReminderService> _log;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(15) };
    private readonly HashSet<string> _told = new();
    private System.Windows.Forms.NotifyIcon? _icon;
    private DateOnly _toldDay;

    public ReminderService(TodayService today, SettingsService settings, Repository repo, IServiceProvider services, ILogger<ReminderService> log)
    { _today = today; _settings = settings; _repo = repo; _services = services; _log = log; _timer.Tick += async (_, _) => await CheckAsync(); }

    public void Start()
    {
        EnsureIcon();
        _timer.Start();
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        first.Tick += async (_, _) => { first.Stop(); await CheckAsync(); };
        first.Start();
    }

    private void EnsureIcon()
    {
        if (_icon is not null) return;
        try
        {
            _icon = new System.Windows.Forms.NotifyIcon { Text = "Garden Helper", Visible = true, Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application };
            void Open(object? s, EventArgs e) => Application.Current.Dispatcher.Invoke(() =>
            {
                if (Application.Current.MainWindow is { } w) { if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal; w.Show(); w.Activate(); }
                _services.GetRequiredService<ViewModels.ShellViewModel>().GoTo<ViewModels.TodayViewModel>();
            });
            _icon.BalloonTipClicked += Open; _icon.MouseClick += Open;
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not create the notification icon"); }
    }

    private void Show(string title, string text)
    {
        EnsureIcon();
        try { _icon?.ShowBalloonTip(10000, title, text, System.Windows.Forms.ToolTipIcon.Info); } catch (Exception ex) { _log.LogWarning(ex, "Notification failed"); }
    }

    public Task TestAsync() { Show("Garden Helper", Loc.T("rem_test_text")); return Task.CompletedTask; }

    private static string Lines(IEnumerable<string> titles)
    {
        var list = titles.ToList(); var shown = list.Take(3).ToList();
        return string.Join("\n", shown) + (list.Count > 3 ? "\n" + Loc.F("rem_more", list.Count - 3) : "");
    }

    /// <summary>Looks at today's list: tells about new urgent things, and gives the daily summary once.</summary>
    public async Task CheckAsync()
    {
        try
        {
            if (await _settings.GetAsync(EnabledKey) == "0") return;
            var garden = await _repo.ActiveGardenAsync(); if (garden is null) return;
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (_toldDay != today) { _told.Clear(); _toldDay = today; }
            var items = await _today.BuildAsync(garden, await _today.CachedForecastAsync());

            var fresh = items.Where(i => i.Severity == "urgent" && _told.Add(i.Key + "|" + string.Join("|", i.Args))).ToList();
            if (fresh.Count > 0) Show(Loc.T("rem_urgent_title"), Lines(fresh.Select(TodayService.TitleOf)));

            var summaryTime = TimeOnly.TryParse(await _settings.GetAsync(SummaryKey) ?? "08:00", out var t) ? t : new TimeOnly(8, 0);
            if (TimeOnly.FromDateTime(DateTime.Now) >= summaryTime && await _settings.GetAsync(LastSummaryKey) != today.ToString("O"))
            {
                await _settings.SetAsync(LastSummaryKey, today.ToString("O"));
                if (items.Count > 0)
                    Show(Loc.F("rem_summary_title", items.Count, items.Count(i => i.Severity == "urgent")), Lines(items.Select(TodayService.TitleOf)));
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Reminder check failed"); }
    }

    public void Dispose() { _timer.Stop(); if (_icon is not null) { _icon.Visible = false; _icon.Dispose(); } }
}
