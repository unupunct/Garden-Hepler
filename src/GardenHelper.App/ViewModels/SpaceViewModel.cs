using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Services;
using GardenHelper.Core.Site;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record LogKindOption(string Key, string Label)
{
    public override string ToString() => Label;
}
public sealed record LogRow(FeatureLog Log, string KindLabel, string DateText);
public sealed record StatusLine(string Bed, string Plant, string Quantity, string Status, string Expected);

/// <summary>One place's own module: notes, history (growth, soil and plant treatments, maintenance), what is planted and what to do next.</summary>
public sealed partial class SpaceViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly AlertService _alerts;
    private readonly KnowledgeBase _kb;
    private readonly IDialogs _dialogs;
    private readonly INavigator _nav;
    private PropertyFeature? _feature;

    public SpaceViewModel(Repository repo, AlertService alerts, KnowledgeBase kb, IDialogs dialogs, INavigator nav, ILogger<SpaceViewModel> log) : base(log)
    {
        _repo = repo; _alerts = alerts; _kb = kb; _dialogs = dialogs; _nav = nav;
        NewLogDate = DateTime.Today;
    }

    public int FeatureId { get; set; }
    public ObservableCollection<LogKindOption> LogKinds { get; } = new();
    public ObservableCollection<LogRow> Logs { get; } = new();
    public ObservableCollection<StatusLine> Plantings { get; } = new();
    public ObservableCollection<AlertRow> Alerts { get; } = new();

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _kindLabel = "";
    [ObservableProperty] private string _areaText = "";
    [ObservableProperty] private bool _isStructure;
    [ObservableProperty] private bool _holdsPlantings;
    [ObservableProperty] private bool _hasBed;
    [ObservableProperty] private LogKindOption? _newLogKind;
    [ObservableProperty] private string _newLogText = "";
    [ObservableProperty] private DateTime? _newLogDate;
    [ObservableProperty] private string _nextText = "";

    public override Task LoadAsync() => RunAsync(async () =>
    {
        _feature = (await _repo.ListAsync<PropertyFeature>(q => q.Where(f => f.Id == FeatureId))).FirstOrDefault();
        if (_feature is null) { ErrorMessage = Loc.T("space_missing"); return; }
        Name = _feature.Name; Notes = _feature.Notes ?? "";
        KindLabel = Loc.T("feat_" + _feature.Kind);
        IsStructure = FeatureCatalog.IsStructure(_feature.Kind);
        HoldsPlantings = FeatureCatalog.HoldsPlantings(_feature.Kind);
        var (area, perimeter) = GeoMath.Measure(GeoMath.FromJson(_feature.PointsJson));
        AreaText = Loc.F("space_area", area.ToString("N0", Loc.Instance.Culture), (area / 100).ToString("N1", Loc.Instance.Culture), perimeter.ToString("N0", Loc.Instance.Culture));

        LogKinds.Clear();
        foreach (var k in FeatureCatalog.LogKindsFor(_feature.Kind)) LogKinds.Add(new LogKindOption(k, Loc.T("log_" + k)));
        NewLogKind ??= LogKinds.FirstOrDefault();
        if (NewLogKind is not null && LogKinds.All(k => k.Key != NewLogKind.Key)) NewLogKind = LogKinds.First();

        await ReloadLogsAsync();

        var beds = await _repo.ListAsync<GardenBed>(q => q.Where(b => b.FeatureId == FeatureId).Include(b => b.Plantings));
        HasBed = beds.Count > 0;
        Plantings.Clear();
        var lang = Loc.Instance.Language; var c = Loc.Instance.Culture;
        foreach (var bed in beds)
            foreach (var p in bed.Plantings.Where(p => p.Status != PlantingStatus.Finished).OrderBy(p => p.Status))
            {
                var plant = _kb.Get(p.PlantId);
                var exp = p.PlantedDate is { } d && plant?.DaysToMaturity is { } m
                    ? Loc.F("design_expected", d.AddDays(m.Min).ToString("d MMM", c), d.AddDays(m.Max).ToString("d MMM", c)) : "";
                Plantings.Add(new StatusLine(bed.Name, plant?.DisplayName(lang) ?? p.PlantId, "×" + p.Quantity, Loc.T("status_" + p.Status), exp));
            }

        Alerts.Clear();
        foreach (var a in (await _alerts.LoadAsync()).Where(a => beds.Any(b => a.Alert.Args.Contains(b.Name)))) Alerts.Add(a);
        NextText = Alerts.Count > 0 ? "" : HoldsPlantings ? (HasBed ? Loc.T("space_nothing_due") : Loc.T("space_plan_first"))
            : Loc.T("space_log_hint");
    });

    private async Task ReloadLogsAsync()
    {
        Logs.Clear();
        var logs = await _repo.ListAsync<FeatureLog>(q => q.Where(l => l.FeatureId == FeatureId).OrderByDescending(l => l.Date).ThenByDescending(l => l.Id));
        foreach (var l in logs) Logs.Add(new LogRow(l, Loc.T("log_" + l.Kind), l.Date.ToString("d MMM yyyy", Loc.Instance.Culture)));
    }

    [RelayCommand]
    private Task SaveDetailsAsync() => RunAsync(async () =>
    {
        if (_feature is null || string.IsNullOrWhiteSpace(Name)) { ErrorMessage = Loc.T("space_name_required"); return; }
        _feature.Name = Name.Trim(); _feature.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes;
        var logs = _feature.Logs; _feature.Logs = new();
        _feature = await _repo.SaveAsync(_feature);
        _feature.Logs = logs;
        StatusMessage = Loc.T("saved");
    }, busy: false);

    [RelayCommand]
    private Task AddLogAsync() => RunAsync(async () =>
    {
        if (_feature is null || NewLogKind is null || string.IsNullOrWhiteSpace(NewLogText)) return;
        await _repo.SaveAsync(new FeatureLog
        {
            FeatureId = FeatureId, Kind = NewLogKind.Key, Text = NewLogText.Trim(),
            Date = Dates.ToDateOnly(NewLogDate) ?? DateOnly.FromDateTime(DateTime.Today),
        });
        NewLogText = "";
        await ReloadLogsAsync();
    }, busy: false);

    [RelayCommand]
    private Task DeleteLogAsync(LogRow row) => RunAsync(async () =>
    {
        if (!_dialogs.Confirm(Loc.T("confirm_delete"))) return;
        row.Log.Feature = null;
        await _repo.DeleteAsync(row.Log);
        await ReloadLogsAsync();
    }, busy: false);

    [RelayCommand] private void Back() => _nav.GoTo<PropertyViewModel>();
    [RelayCommand] private void EditOnMap() { var id = FeatureId; _nav.GoTo<MapViewModel>(vm => vm.PendingSelect = id); }
    [RelayCommand] private void PlanInDesigner() { var id = FeatureId; _nav.GoTo<DesignerViewModel>(vm => vm.PlanFeatureId = id); }
    [RelayCommand] private void OpenDesigner() => _nav.GoTo<DesignerViewModel>();
}
