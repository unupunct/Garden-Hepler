using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Greenhouse;
using GardenHelper.Core.Planning;
using GardenHelper.Core.Services;
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

public sealed record TodayGroup(string Title, ObservableCollection<TodayRow> Rows);

/// <summary>The first screen of the day: everything that is late or due, with a button that does it.</summary>
public sealed partial class TodayViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly AlertService _alerts;
    private readonly GreenhouseService _greenhouse;
    private readonly INavigator _nav;
    private readonly ILogger _logger;

    public TodayViewModel(Repository repo, AlertService alerts, GreenhouseService greenhouse, INavigator nav, ILogger<TodayViewModel> log) : base(log)
    {
        _logger = log; _repo = repo; _alerts = alerts; _greenhouse = greenhouse; _nav = nav;
    }

    public ObservableCollection<TodayGroup> Groups { get; } = new();
    [ObservableProperty] private string _greeting = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _isCalm;

    private DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var culture = Loc.Instance.Culture; var now = DateTime.Now;
        Greeting = Loc.F(now.Hour < 12 ? "today_morning" : now.Hour < 18 ? "today_afternoon" : "today_evening", now.ToString("dddd, d MMMM", culture));
        var garden = await _repo.ActiveGardenAsync();
        var tasks = await _repo.ListAsync<GardenTask>(q => q.Where(t => !t.Done && t.DueDate <= Today));
        var plants = await _repo.ListAsync<InventoryPlant>(q => q.Where(p => !p.IsTree));
        var groups = await _repo.ListAsync<AnimalGroup>(q => q.Include(g => g.Logs).Include(g => g.Rations));
        var items = TodayPlanner.Build(Today, tasks, plants, groups, "d MMM");

        // what the garden rules and the greenhouse sensors say
        foreach (var row in await _alerts.LoadAsync())
            if (row.Alert.Severity is AlertSeverity.Urgent or AlertSeverity.Soon)
                items.Add(new TodayItem("advice", row.Alert.Severity == AlertSeverity.Urgent ? "urgent" : "soon", row.Glyph, "today_advice", new[] { row.Message }, TodayAction.OpenActions, row.Alert.PlantingId ?? 0));
        if (garden is not null)
            foreach (var f in await _repo.ListAsync<PropertyFeature>(q => q.Where(x => x.GardenId == garden.Id && x.Kind == "greenhouse")))
            {
                try
                {
                    var setup = await _greenhouse.SetupAsync(f.Id); var latest = await _greenhouse.LatestAsync(f.Id);
                    DateTime? last = latest.Count == 0 ? null : latest.Values.Max(v => v.AtUtc);
                    foreach (var a in GreenhouseRules.Evaluate(setup, latest.ToDictionary(kv => kv.Key, kv => kv.Value.Value), last, DateTime.UtcNow).Where(a => a.Severity is "critical" or "warning"))
                        items.Add(new TodayItem("greenhouse", a.Severity == "critical" ? "urgent" : "soon", a.Severity == "critical" ? "⛔" : "⚠", "today_greenhouse", new[] { f.Name, Loc.F(a.Key, a.Args) }, TodayAction.OpenGreenhouse, f.Id));
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Greenhouse alerts failed"); }
            }

        items = items.OrderBy(i => TodayPlanner.SeverityRank(i.Severity)).ThenBy(i => Array.IndexOf(TodayPlanner.Sections, i.Section)).ToList();
        Groups.Clear();
        foreach (var sec in TodayPlanner.Sections)
        {
            var rows = items.Where(i => i.Section == sec).Select(i => new TodayRow(i)).ToList();
            if (rows.Count > 0) Groups.Add(new TodayGroup(Loc.T("today_s_" + sec), new ObservableCollection<TodayRow>(rows)));
        }
        Refresh();
    });

    private void Refresh()
    {
        var open = Groups.SelectMany(g => g.Rows).Where(r => !r.IsDone).ToList();
        var urgent = open.Count(r => r.IsUrgent);
        IsCalm = open.Count == 0;
        Summary = open.Count == 0 ? Loc.T("today_calm") : Loc.F("today_summary", open.Count, urgent);
    }

    [RelayCommand]
    private Task DoAsync(TodayRow row) => RunAsync(async () =>
    {
        var it = row.Item;
        switch (it.Action)
        {
            case TodayAction.Done:
                if ((await _repo.ListAsync<GardenTask>(q => q.Where(t => t.Id == it.RefId))).FirstOrDefault() is { } t) { t.Done = true; t.CompletedUtc = DateTime.UtcNow; await _repo.SaveAsync(t); }
                break;
            case TodayAction.Watered:
                if ((await _repo.ListAsync<InventoryPlant>(q => q.Where(p => p.Id == it.RefId))).FirstOrDefault() is { } p)
                {
                    p.LastWatered = Today; await _repo.SaveAsync(p);
                    await _repo.SaveAsync(new InventoryLog { PlantId = p.Id, Date = Today, Kind = "watering", Text = "" });
                }
                break;
            case TodayAction.LogProduction:
                if (!double.TryParse(row.Input.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var qty) || qty < 0) { ErrorMessage = Loc.T("animal_need_quantity"); return; }
                await _repo.SaveAsync(new AnimalLog { GroupId = it.RefId, Date = Today, Kind = "production", Quantity = qty, Text = "" });
                break;
            case TodayAction.OpenAnimals: _nav.GoTo<AnimalsViewModel>(); return;
            case TodayAction.OpenGreenhouse: _nav.GoTo<GreenhouseViewModel>(); return;
            case TodayAction.OpenActions: _nav.GoTo<MonitorViewModel>(); return;
            case TodayAction.OpenInventory: _nav.GoTo<InventoryViewModel>(); return;
        }
        row.IsDone = true; Refresh();
        StatusMessage = Loc.T("today_done_msg");
    }, busy: false);
}
