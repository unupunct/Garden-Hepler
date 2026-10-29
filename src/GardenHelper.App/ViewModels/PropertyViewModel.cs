using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Design;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Services;
using GardenHelper.Core.Site;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record PlanShape(int FeatureId, string Name, string Kind, string KindLabel, PointCollection Points, double LabelX, double LabelY,
    Brush Fill, Brush Stroke, double StrokeWidth, bool IsOutline)
{
    public bool Clickable => !IsOutline;
    public string Tip => IsOutline ? Name : $"{Name} · {KindLabel}";
}

public sealed record PlaceCard(int Id, string Name, string Kind, string KindLabel, string AreaText, string Summary, string Alerts, bool HasAlerts, Brush Swatch)
{
    public override string ToString() => $"{Name}, {KindLabel}";
}

/// <summary>The command center: the whole property as a plan, with a card for every place that opens its own module.</summary>
public sealed partial class PropertyViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly AlertService _alerts;
    private readonly KnowledgeBase _kb;
    private readonly INavigator _nav;
    private List<PlaceCard> _all = new();

    public PropertyViewModel(Repository repo, AlertService alerts, KnowledgeBase kb, INavigator nav, ILogger<PropertyViewModel> log) : base(log)
    {
        _repo = repo; _alerts = alerts; _kb = kb; _nav = nav;
    }

    public ObservableCollection<PlanShape> Shapes { get; } = new();
    public ObservableCollection<PlaceCard> Cards { get; } = new();

    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private double _canvasWidth = 100;
    [ObservableProperty] private double _canvasHeight = 100;
    [ObservableProperty] private double _labelSize = 12;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string _gardenName = "";
    [ObservableProperty] private string _placesText = "";
    [ObservableProperty] private string _areaText = "";
    [ObservableProperty] private string _alertsText = "";
    [ObservableProperty] private bool _hasAlerts;

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var f = Core.Library.SubjectExtractor.Fold(Filter ?? "");
        Cards.Clear();
        foreach (var c in _all.Where(c => f.Length == 0 || Core.Library.SubjectExtractor.Fold(c.Name + " " + c.KindLabel + " " + c.Summary).Contains(f)))
            Cards.Add(c);
    }

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var garden = await _repo.ActiveGardenAsync();
        Shapes.Clear(); _all = new();
        GardenName = garden?.Name ?? "";
        if (garden is null) { HasData = false; ApplyFilter(); return; }

        var outline = GeoMath.FromJson(garden.BoundaryJson);
        var features = await _repo.ListAsync<PropertyFeature>(q => q.Where(x => x.GardenId == garden.Id).Include(x => x.Logs).OrderBy(x => x.Name));
        var shapes = new List<IReadOnlyList<GeoPoint>>();
        if (outline.Count >= GeoMath.MinBoundaryPins) shapes.Add(outline);
        var usable = features.Select(f => (Feature: f, Pins: GeoMath.FromJson(f.PointsJson))).Where(x => x.Pins.Count >= GeoMath.MinBoundaryPins).ToList();
        shapes.AddRange(usable.Select(x => (IReadOnlyList<GeoPoint>)x.Pins));
        HasData = shapes.Count > 0;
        if (!HasData) { ApplyFilter(); return; }

        var (plan, w, h) = GeoMath.ToPlanFittedMany(shapes, 500);
        CanvasWidth = w; CanvasHeight = h; LabelSize = Math.Max(w, h) / 62;
        var i = 0;
        PlanShape Make(int id, string name, string kind, string label, List<Pt> pts, bool isOutline, double stroke)
        {
            var col = FeatureColors.For(kind);
            var fill = new SolidColorBrush(Color.FromArgb(isOutline ? (byte)14 : (byte)120, col.R, col.G, col.B)); fill.Freeze();
            var line = new SolidColorBrush(col); line.Freeze();
            return new PlanShape(id, name, kind, label, new PointCollection(pts.Select(p => new Point(p.X, p.Y))),
                pts.Average(p => p.X), pts.Average(p => p.Y), fill, line, stroke, isOutline);
        }
        if (outline.Count >= GeoMath.MinBoundaryPins) Shapes.Add(Make(0, Loc.T("feat_outline"), "outline", Loc.T("feat_outline"), plan[i++], true, LabelSize / 5));

        var beds = await _repo.ListAsync<GardenBed>(q => q.Where(b => b.GardenId == garden.Id && b.FeatureId != null).Include(b => b.Plantings));
        var alerts = await _alerts.LoadAsync();
        var lang = Loc.Instance.Language; var culture = Loc.Instance.Culture;
        foreach (var (feature, pins) in usable)
        {
            Shapes.Add(Make(feature.Id, feature.Name, feature.Kind, Loc.T("feat_" + feature.Kind), plan[i++], false, LabelSize / 8));
            var (area, _) = GeoMath.Measure(pins);
            var myBeds = beds.Where(b => b.FeatureId == feature.Id).ToList();
            var active = myBeds.SelectMany(b => b.Plantings).Where(p => p.Status != PlantingStatus.Finished).ToList();
            var myAlerts = alerts.Count(a => myBeds.Any(b => a.Alert.Args.Contains(b.Name)));
            var last = feature.Logs.OrderByDescending(l => l.Date).ThenByDescending(l => l.Id).FirstOrDefault();
            var summary = new List<string>();
            if (active.Count > 0) summary.Add(Loc.F("prop_plantings", active.Count, string.Join(", ", active.Take(3).Select(p => _kb.Get(p.PlantId)?.DisplayName(lang) ?? p.PlantId))));
            if (last is not null) summary.Add(Loc.F("prop_last_log", last.Date.ToString("d MMM", culture), Loc.T("log_" + last.Kind), last.Text.Length > 60 ? last.Text[..60] + "…" : last.Text));
            if (summary.Count == 0) summary.Add(FeatureCatalog.IsStructure(feature.Kind) ? Loc.T("prop_structure") : Loc.T("prop_empty_space"));
            var col = FeatureColors.For(feature.Kind); var sw = new SolidColorBrush(col); sw.Freeze();
            _all.Add(new PlaceCard(feature.Id, feature.Name, feature.Kind, Loc.T("feat_" + feature.Kind), Loc.F("prop_area", area.ToString("N0", culture)),
                string.Join("\n", summary), myAlerts > 0 ? Loc.F("prop_alerts", myAlerts) : "", myAlerts > 0, sw));
        }
        ApplyFilter();

        var total = outline.Count >= GeoMath.MinBoundaryPins ? GeoMath.Measure(outline).AreaM2 : 0;
        PlacesText = Loc.F("prop_places", _all.Count);
        AreaText = total > 0 ? Loc.F("prop_property_area", total.ToString("N0", culture), (total / 10000).ToString("0.00", culture)) : Loc.T("prop_no_outline");
        HasAlerts = alerts.Count > 0;
        AlertsText = alerts.Count > 0 ? Loc.F("prop_total_alerts", alerts.Count(a => a.Alert.Severity != AlertSeverity.Info)) : Loc.T("prop_no_alerts");
    });

    public void OpenPlace(int id) { if (id > 0) _nav.GoTo<SpaceViewModel>(vm => vm.FeatureId = id); }
    [RelayCommand] private void OpenCard(PlaceCard card) => OpenPlace(card.Id);
    [RelayCommand] private void OpenMap() => _nav.GoTo<MapViewModel>();
    [RelayCommand] private void OpenDesigner() => _nav.GoTo<DesignerViewModel>();
    [RelayCommand] private void OpenMonitor() => _nav.GoTo<MonitorViewModel>();
}
