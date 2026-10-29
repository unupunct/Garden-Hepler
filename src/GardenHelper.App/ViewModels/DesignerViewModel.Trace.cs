using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Design;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Site;

namespace GardenHelper.App.ViewModels;

public sealed record TracePin(double X, double Y);

// Satellite layer strength, and tracing a building or space (any shape) straight on the satellite picture.
public sealed partial class DesignerViewModel
{
    [ObservableProperty] private double _backdropOpacity = 1;
    public string BackdropOpacityText => $"{Math.Round(BackdropOpacity * 100)}%";
    partial void OnBackdropOpacityChanged(double value) => OnPropertyChanged(nameof(BackdropOpacityText));
    [RelayCommand] private void BackdropMore() => BackdropOpacity = Math.Min(1, Math.Round(BackdropOpacity + 0.1, 2));
    [RelayCommand] private void BackdropLess() => BackdropOpacity = Math.Max(0.1, Math.Round(BackdropOpacity - 0.1, 2));

    private readonly List<TracePin> _trace = new();
    public ObservableCollection<TracePin> TracePins { get; } = new();
    public IReadOnlyList<KindOption> TraceKinds { get; } = FeatureCatalog.Structures.Concat(FeatureCatalog.Spaces).Select(k => new KindOption(k, Loc.T("feat_" + k))).ToList();
    [ObservableProperty] private bool _tracing;
    [ObservableProperty] private PointCollection _traceShape = new();
    [ObservableProperty] private string _traceName = "";
    [ObservableProperty] private KindOption? _traceKind;
    [ObservableProperty] private string _traceInfo = "";

    [RelayCommand]
    private void StartTrace()
    {
        if (_frame is null) { ErrorMessage = Loc.T("trace_need_map"); return; }
        CancelTrace(); Tracing = true; TraceKind = TraceKinds.First(); Selected = null;
        TraceInfo = Loc.F("trace_pins", 0);
    }

    public void AddTracePoint(Point p)
    {
        if (!Tracing || _trace.Count >= 40) return;
        _trace.Add(new TracePin(Math.Round(p.X), Math.Round(p.Y))); RefreshTrace();
    }

    [RelayCommand] private void UndoPin() { if (_trace.Count > 0) { _trace.RemoveAt(_trace.Count - 1); RefreshTrace(); } }

    private void RefreshTrace()
    {
        TracePins.Clear(); foreach (var t in _trace) TracePins.Add(t);
        TraceShape = new PointCollection(_trace.Select(t => new Point(t.X, t.Y)));
        TraceInfo = Loc.F("trace_pins", _trace.Count);
    }

    [RelayCommand]
    private void CancelTrace() { _trace.Clear(); RefreshTrace(); Tracing = false; TraceName = ""; }

    /// <summary>Saves the pins as a place on the property map: the shape is kept exactly as traced.</summary>
    [RelayCommand]
    private Task FinishTraceAsync() => RunAsync(async () =>
    {
        if (_garden is null || _frame is null) return;
        if (_trace.Count < GeoMath.MinBoundaryPins) { ErrorMessage = Loc.T("trace_need_pins"); return; }
        if (string.IsNullOrWhiteSpace(TraceName)) { ErrorMessage = Loc.T("trace_need_name"); return; }
        var geo = _trace.Select(p => _frame.ToGeo(new Pt(p.X, p.Y))).ToList();
        if (GeoMath.SelfIntersects(geo)) { ErrorMessage = Loc.T("trace_crossing"); return; }
        var name = TraceName.Trim();
        await _repo.SaveAsync(new PropertyFeature { GardenId = _garden.Id, Name = name, Kind = (TraceKind ?? TraceKinds.First()).Key, PointsJson = GeoMath.ToJson(geo) });
        var cx = _trace.Average(t => t.X); var cy = _trace.Average(t => t.Y); var keep = Scale;
        CancelTrace();
        await LoadAsync();
        Scale = keep; RequestCenter(cx, cy);          // stay where you were working
        StatusMessage = Loc.F("trace_saved", name);
    }, busy: false);

    /// <summary>Double-clicking a traced place: buildings open their plan, garden spaces become a bed to plant.</summary>
    [RelayCommand]
    private void OpenShape(FeatureShape shape)
    {
        if (shape.IsStructure || shape.Kind == "greenhouse") _nav.GoTo<InteriorViewModel>(vm => vm.FeatureId = shape.Id);
        else _ = PlanFeatureCoreAsync(shape.Id);
    }
}
