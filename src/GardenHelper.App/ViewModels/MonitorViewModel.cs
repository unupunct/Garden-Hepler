using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record OverviewRow(string Bed, string Plant, int Quantity, string Status, string Planted, string Expected);

public sealed partial class MonitorViewModel : PageViewModel
{
    private readonly AlertService _alerts;
    private readonly Repository _repo;
    private readonly KnowledgeBase _kb;
    private readonly INavigator _nav;

    public MonitorViewModel(AlertService alerts, Repository repo, KnowledgeBase kb, INavigator nav, ILogger<MonitorViewModel> log) : base(log)
    {
        _alerts = alerts; _repo = repo; _kb = kb; _nav = nav;
    }

    public ObservableCollection<AlertRow> Alerts { get; } = new();
    public ObservableCollection<OverviewRow> Overview { get; } = new();
    [ObservableProperty] private bool _hasGarden;
    [ObservableProperty] private bool _hasBeds;
    [ObservableProperty] private string _summary = "";

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var garden = await _repo.ActiveGardenAsync();
        HasGarden = garden is not null;
        Alerts.Clear(); Overview.Clear();
        if (garden is null) return;
        foreach (var a in await _alerts.LoadAsync()) Alerts.Add(a);

        var beds = await _repo.ListAsync<GardenBed>(q => q.Where(b => b.GardenId == garden.Id).Include(b => b.Plantings).OrderBy(b => b.Name));
        HasBeds = beds.Count > 0;
        var lang = Loc.Instance.Language; var c = Loc.Instance.Culture;
        foreach (var bed in beds)
            foreach (var p in bed.Plantings.Where(p => p.Status != PlantingStatus.Finished).OrderBy(p => p.Status))
            {
                var plant = _kb.Get(p.PlantId);
                var exp = p.PlantedDate is { } d && plant?.DaysToMaturity is { } m
                    ? $"{d.AddDays(m.Min).ToString("d MMM", c)} – {d.AddDays(m.Max).ToString("d MMM", c)}" : "";
                Overview.Add(new OverviewRow(bed.Name, plant?.DisplayName(lang) ?? p.PlantId, p.Quantity, Loc.T("status_" + p.Status), p.PlantedDate?.ToString("d MMM yyyy", c) ?? "", exp));
            }
        Summary = Loc.F("monitor_summary", Alerts.Count(a => a.Alert.Severity == AlertSeverity.Urgent), Alerts.Count(a => a.Alert.Severity == AlertSeverity.Soon), Overview.Count);
    });

    [RelayCommand]
    private Task AddTaskAsync(AlertRow row) => RunAsync(async () =>
    {
        var due = row.Alert.Due is { } d && d >= DateOnly.FromDateTime(DateTime.Today) ? d : DateOnly.FromDateTime(DateTime.Today);
        await _repo.SaveAsync(new GardenTask { Title = row.Message, DueDate = due, PlantId = row.Alert.PlantId, Activity = "Other", Notes = row.Why });
        StatusMessage = Loc.T("task_added");
    }, busy: false);

    [RelayCommand] private void OpenDesigner() => _nav.GoTo<DesignerViewModel>();
    [RelayCommand] private void OpenGarden() => _nav.GoTo<GardenViewModel>();
}
