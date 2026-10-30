using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Greenhouse;
using GardenHelper.Core.Planning;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Services;
using GardenHelper.Core.Site;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed partial class TodayRow : ObservableObject
{
    public TodayRow(TodayItem item) { Item = item; Title = Loc.F(item.Key, item.Args.Cast<object>().ToArray()); }
    public TodayItem Item { get; }
    public string Title { get; }
    public string Glyph => Item.Glyph;
    public bool IsUrgent => Item.Severity == "urgent";
    public bool NeedsNumber => Item.NeedsNumber;
    public string ActionLabel => Loc.T("today_a_" + Item.Action);
    public string NumberHint => Item.Unit is null ? "" : Loc.T("today_unit_" + Item.Unit);
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private bool _isDone;
}

public sealed record DayCard(string Label, string Glyph, string Range, string Rain);
public sealed record RipeningRow(string Title, string When, bool Ready);

/// <summary>One section of the list. A long section shows a page of rows and a button for the rest, so a property with hundreds of late tasks stays quick and readable.</summary>
public sealed partial class TodayGroup : ObservableObject
{
    private const int PageSize = 25;
    public TodayGroup(string title, IReadOnlyList<TodayRow> all)
    {
        Title = title; All = all;
        foreach (var row in all.Take(PageSize)) Rows.Add(row);
        Update();
    }
    public string Title { get; }
    public IReadOnlyList<TodayRow> All { get; }
    public ObservableCollection<TodayRow> Rows { get; } = new();
    [ObservableProperty] private int _hidden;
    public bool HasMore => Hidden > 0;
    public string MoreText => Loc.F("today_show_more", Hidden);
    private void Update() { Hidden = All.Count - Rows.Count; OnPropertyChanged(nameof(HasMore)); OnPropertyChanged(nameof(MoreText)); }
    [RelayCommand] private void ShowMore() { foreach (var row in All.Skip(Rows.Count).Take(PageSize)) Rows.Add(row); Update(); }
}

/// <summary>The first screen of the day: everything that is late or due, with a button that does it.</summary>
public sealed partial class TodayViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly TodayService _todayService;
    private readonly INavigator _nav;
    private readonly ILogger _logger;
    private readonly SettingsService _settings;
    private readonly KnowledgeBase _kb;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private const string WeatherKey = TodayService.WeatherKey;
    private Garden? _garden;
    private WeatherForecast? _forecast;

    public TodayViewModel(Repository repo, TodayService todayService, SettingsService settings, KnowledgeBase kb, INavigator nav, ILogger<TodayViewModel> log) : base(log)
    {
        _settings = settings; _kb = kb; _logger = log; _repo = repo; _todayService = todayService; _nav = nav;
    }

    public ObservableCollection<TodayGroup> Groups { get; } = new();
    [ObservableProperty] private string _greeting = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _isCalm;
    public ObservableCollection<DayCard> DayCards { get; } = new();
    public ObservableCollection<RipeningRow> Ripenings { get; } = new();
    [ObservableProperty] private int _urgentCount, _openCount, _waterCount, _ripeningCount;
    [ObservableProperty] private bool _hasSun, _hasWeather, _hasRipenings, _hasFrost, _noLocation;
    [ObservableProperty] private string _sunUp = "", _sunDown = "", _sunDetail = "", _weatherNow = "", _weatherToday = "", _weatherAge = "", _frostText = "";

    private DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var culture = Loc.Instance.Culture; var now = DateTime.Now;
        Greeting = Loc.F(now.Hour < 12 ? "today_morning" : now.Hour < 18 ? "today_afternoon" : "today_evening", now.ToString("dddd, d MMMM", culture));
        var garden = await _repo.ActiveGardenAsync(); _garden = garden;
        _forecast = WeatherForecast.FromJson(await _settings.GetAsync(WeatherKey));
        var beds = garden is null ? new List<GardenBed>() : await _repo.ListAsync<GardenBed>(q => q.Where(b => b.GardenId == garden.Id).Include(b => b.Plantings));
        BuildGlance(garden, beds);
        var items = await _todayService.BuildAsync(garden, _forecast);
        Groups.Clear();
        foreach (var sec in TodayPlanner.Sections)
        {
            var rows = items.Where(i => i.Section == sec).Select(i => new TodayRow(i)).ToList();
            if (rows.Count > 0) Groups.Add(new TodayGroup(Loc.T("today_s_" + sec), rows));
        }
        Refresh();
    });

    /// <summary>Sun times, forecast, frost dates and what is nearly ready to harvest.</summary>
    private void BuildGlance(Garden? g, IReadOnlyList<GardenBed> beds)
    {
        var culture = Loc.Instance.Culture;
        NoLocation = g?.Latitude is null || g.Longitude is null;
        HasSun = !NoLocation;
        if (HasSun)
        {
            var lat = g!.Latitude!.Value; var lon = g.Longitude!.Value;
            var d = SolarCalculator.Day(lat, lon, Today, SolarCalculator.GuessOffset(lon, Today));
            var y = SolarCalculator.Day(lat, lon, Today.AddDays(-1), SolarCalculator.GuessOffset(lon, Today.AddDays(-1)));
            SunUp = d.Sunrise?.ToString("HH:mm") ?? "–"; SunDown = d.Sunset?.ToString("HH:mm") ?? "–";
            var len = TimeSpan.FromHours(d.DayLengthHours); var delta = (int)Math.Round((d.DayLengthHours - y.DayLengthHours) * 60);
            SunDetail = Loc.F("today_daylength", (int)len.TotalHours, len.Minutes) + " · " + (delta == 0 ? "" : Loc.F(delta > 0 ? "today_longer" : "today_shorter", Math.Abs(delta)));
        }

        DayCards.Clear();
        HasWeather = _forecast is { Days.Count: > 0 };
        if (_forecast is { } f && HasWeather)
        {
            var now = f.CurrentTempC is { } t ? $"{t:0}°C " : "";
            var cur = f.CurrentCode is { } c ? WeatherCodes.Describe(c) : ("", "");
            WeatherNow = now + cur.Item2 + " " + (cur.Item1.Length > 0 ? Loc.T(cur.Item1) : "");
            var td = f.Days.FirstOrDefault(x => x.Date == Today) ?? f.Days[0];
            WeatherToday = Loc.F("today_wx_line", td.MinC.ToString("0", culture), td.MaxC.ToString("0", culture), td.PrecipProbPct, td.WindMaxKmh.ToString("0", culture));
            var age = DateTime.UtcNow - f.FetchedUtc;
            WeatherAge = age.TotalHours < 1 ? Loc.T("today_wx_fresh") : age.TotalHours < 48 ? Loc.F("today_wx_hours", (int)age.TotalHours) : Loc.F("today_wx_days", (int)age.TotalDays);
            foreach (var d in f.Days.Where(x => x.Date >= Today).Take(7))
                DayCards.Add(new DayCard(d.Date == Today ? Loc.T("today_wx_today") : d.Date.ToString("ddd", culture), WeatherCodes.Describe(d.Code).Glyph, $"{d.MinC:0}° / {d.MaxC:0}°", d.PrecipProbPct >= 20 ? $"{d.PrecipProbPct}%" : ""));
        }

        // the next frost date in the calendar
        HasFrost = false;
        if (g is not null)
        {
            var next = new List<(DateOnly Date, string Key)>();
            if (g.LastSpringFrost is { } ls) { next.Add((new DateOnly(Today.Year, ls.Month, ls.Day), "today_frost_last")); next.Add((new DateOnly(Today.Year + 1, ls.Month, ls.Day), "today_frost_last")); }
            if (g.FirstAutumnFrost is { } fa) next.Add((new DateOnly(Today.Year, fa.Month, fa.Day), "today_frost_first"));
            if (next.Where(x => x.Date >= Today).OrderBy(x => x.Date).Select(x => ((DateOnly Date, string Key)?)x).FirstOrDefault() is { } n && n.Date.DayNumber - Today.DayNumber <= 200)
            { FrostText = Loc.F(n.Key, n.Date.ToString("d MMM", culture), n.Date.DayNumber - Today.DayNumber); HasFrost = true; }
        }

        Ripenings.Clear();
        foreach (var r in HarvestForecast.Upcoming(beds, _kb, Today).Take(8))
        {
            var name = _kb.Get(r.PlantId)?.DisplayName(Loc.Instance.Language) ?? r.PlantId;
            var when = r.ReadyNow ? Loc.T("today_ready_now")
                : r.DaysToEarliest >= 14 ? Loc.F("today_ready_weeks", (int)Math.Round(r.DaysToEarliest / 7.0), r.Earliest.ToString("d MMM", culture))
                : Loc.FN("today_ready_days", r.DaysToEarliest, r.DaysToEarliest, r.Earliest.ToString("d MMM", culture));
            Ripenings.Add(new RipeningRow($"{name} · {r.BedName}", when, r.ReadyNow));
        }
        HasRipenings = Ripenings.Count > 0;
    }

    /// <summary>Downloads the forecast (sends only the coordinates) and keeps it so it still shows offline.</summary>
    [RelayCommand]
    private Task UpdateWeatherAsync() => RunAsync(async () =>
    {
        if (_garden?.Latitude is not { } lat || _garden.Longitude is not { } lon) { ErrorMessage = Loc.T("today_wx_need_location"); return; }
        try
        {
            _forecast = await new WeatherService(Http).FetchAsync(lat, lon);
            await _settings.SetAsync(WeatherKey, _forecast.ToJson());
        }
        catch (Exception ex) { _logger.LogInformation(ex, "Forecast download failed"); ErrorMessage = Loc.T("today_wx_offline"); return; }
        await LoadAsync();
    }, busy: false);

    private void Refresh()
    {
        var open = Groups.SelectMany(g => g.All).Where(r => !r.IsDone).ToList();
        var urgent = open.Count(r => r.IsUrgent);
        IsCalm = open.Count == 0; UrgentCount = urgent; OpenCount = open.Count;
        WaterCount = open.Count(r => r.Item.Action == TodayAction.Watered); RipeningCount = Ripenings.Count(r => r.Ready);
        Summary = open.Count == 0 ? Loc.T("today_calm") : Loc.FN("today_summary", open.Count, open.Count, urgent);
    }

    [RelayCommand]
    private Task DoAsync(TodayRow row) => RunAsync(async () =>
    {
        var it = row.Item;
        switch (it.Action)
        {
            case TodayAction.Done or TodayAction.Watered or TodayAction.WaterBed or TodayAction.AddTask or TodayAction.LogProduction:
            {
                double? qty = null;
                if (it.Action == TodayAction.LogProduction)
                {
                    if (!double.TryParse(row.Input.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var q) || q < 0) { ErrorMessage = Loc.T("animal_need_quantity"); return; }
                    qty = q;
                }
                if (!await _todayService.PerformAsync(it.Action, it.RefId, it.RefIds, qty, row.Title, it.Due, it.PlantId)) { ErrorMessage = Loc.T("today_failed"); return; }
                break;
            }
            case TodayAction.OpenAnimals: _nav.GoTo<AnimalsViewModel>(); return;
            case TodayAction.OpenGreenhouse: _nav.GoTo<GreenhouseViewModel>(); return;
            case TodayAction.OpenActions: _nav.GoTo<MonitorViewModel>(); return;
            case TodayAction.OpenInventory: _nav.GoTo<InventoryViewModel>(); return;
        }
        row.IsDone = true; Refresh();
        StatusMessage = Loc.T("today_done_msg");
    }, busy: false);
}
