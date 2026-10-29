using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Design;
using GardenHelper.Core.Domain;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record MarkPoint(double X, double Y);

/// <summary>A note, line or run drawn on the garden plan: a text label, an irrigation line, a path, a cable run.</summary>
public sealed class MarkShape
{
    public MarkShape(PlanMark mark, double zoomFont)
    {
        Mark = mark;
        var pts = MarkKinds.Points(mark);
        Points = new PointCollection(pts.Select(p => new Point(p.X, p.Y)));
        IsLabel = mark.Kind == "label";
        X = pts.Count > 0 ? pts[0].X : 0; Y = pts.Count > 0 ? pts[0].Y : 0;
        var (color, thick, dash) = MarkKinds.Style(mark.Kind);
        Stroke = Frozen(color); Thickness = thick; Dashes = dash;
        Tip = IsLabel ? mark.Text : $"{Loc.T("mark_k_" + mark.Kind)} · {MarkKinds.LengthMetres(pts):0.#} m" + (string.IsNullOrWhiteSpace(mark.Text) ? "" : " · " + mark.Text);
    }
    public PlanMark Mark { get; }
    public PointCollection Points { get; }
    public bool IsLabel { get; }
    public double X { get; }
    public double Y { get; }
    public Brush Stroke { get; }
    public double Thickness { get; }
    public DoubleCollection Dashes { get; }
    public string Tip { get; }
    public string Text => Mark.Text;
    private static Brush Frozen(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
}

public static class MarkKinds
{
    public static readonly string[] All = { "label", "irrigation", "path", "cable", "measure" };
    public static List<MarkPoint> Points(PlanMark m) { try { return JsonSerializer.Deserialize<List<MarkPoint>>(m.PointsJson) ?? new(); } catch (JsonException) { return new(); } }
    public static double LengthMetres(IReadOnlyList<MarkPoint> pts) { double cm = 0; for (var i = 1; i < pts.Count; i++) cm += Math.Sqrt(Math.Pow(pts[i].X - pts[i - 1].X, 2) + Math.Pow(pts[i].Y - pts[i - 1].Y, 2)); return cm / 100; }
    public static (string Color, double Thickness, DoubleCollection Dash) Style(string kind) => kind switch
    {
        "irrigation" => ("#FF2F7FB5", 8, new DoubleCollection { 3, 2 }), "path" => ("#FFA67C52", 20, new DoubleCollection()), "cable" => ("#FFC0562B", 5, new DoubleCollection { 1, 2 }),
        _ => ("#FF333333", 4, new DoubleCollection()),
    };
}

// Annotating the plan: free-text labels, irrigation, path and cable runs, and a ruler.
public sealed partial class DesignerViewModel
{
    public ObservableCollection<MarkShape> Marks { get; } = new();
    public ObservableCollection<MarkPoint> MarkPins { get; } = new();
    public IReadOnlyList<AnimalOption> MarkTools { get; } = MarkKinds.All.Select(k => new AnimalOption(k, Loc.T("mark_k_" + k))).ToList();
    private readonly List<MarkPoint> _markPts = new();
    [ObservableProperty] private bool _annotating;
    [ObservableProperty] private AnimalOption _markTool = new("label", Loc.T("mark_k_label"));
    [ObservableProperty] private string _markText = "";
    [ObservableProperty] private string _markInfo = "";
    [ObservableProperty] private PointCollection _markShape = new();
    public bool MarkIsLabel => MarkTool.Key == "label";
    public bool MarkNeedsText => MarkTool.Key is "label";
    partial void OnMarkToolChanged(AnimalOption value) { OnPropertyChanged(nameof(MarkIsLabel)); OnPropertyChanged(nameof(MarkNeedsText)); ClearMarkPoints(); }

    private async Task LoadMarksAsync()
    {
        Marks.Clear();
        if (_garden is null) return;
        foreach (var m in await _repo.ListAsync<PlanMark>(q => q.Where(x => x.GardenId == _garden.Id).OrderBy(x => x.Id))) Marks.Add(new MarkShape(m, LabelSize));
    }

    [RelayCommand]
    private void StartAnnotate()
    {
        if (_frame is null) { ErrorMessage = Loc.T("trace_need_map"); return; }
        CancelTrace(); DoneEdit(); Selected = null; Annotating = true; ClearMarkPoints();
    }

    private void ClearMarkPoints() { _markPts.Clear(); MarkPins.Clear(); MarkShape = new PointCollection(); MarkInfo = Loc.T("mark_click"); }

    public void AddMarkPoint(Point p)
    {
        if (!Annotating) return;
        var pt = new MarkPoint(Math.Round(p.X), Math.Round(p.Y));
        if (MarkIsLabel) { _markPts.Clear(); MarkPins.Clear(); }
        _markPts.Add(pt); MarkPins.Clear(); foreach (var x in _markPts) MarkPins.Add(x);
        MarkShape = new PointCollection(_markPts.Select(x => new Point(x.X, x.Y)));
        MarkInfo = MarkIsLabel ? Loc.T("mark_label_placed") : Loc.F("mark_length", MarkKinds.LengthMetres(_markPts).ToString("0.##", Loc.Instance.Culture), _markPts.Count);
    }

    [RelayCommand] private void UndoMarkPoint() { if (_markPts.Count == 0) return; _markPts.RemoveAt(_markPts.Count - 1); MarkPins.Clear(); foreach (var x in _markPts) MarkPins.Add(x); MarkShape = new PointCollection(_markPts.Select(x => new Point(x.X, x.Y))); MarkInfo = Loc.F("mark_length", MarkKinds.LengthMetres(_markPts).ToString("0.##", Loc.Instance.Culture), _markPts.Count); }
    [RelayCommand] private void CancelAnnotate() { ClearMarkPoints(); Annotating = false; MarkText = ""; }

    [RelayCommand]
    private Task FinishMarkAsync() => RunAsync(async () =>
    {
        if (_garden is null) return;
        if (MarkTool.Key == "measure") { StatusMessage = Loc.F("mark_measured", MarkKinds.LengthMetres(_markPts).ToString("0.##", Loc.Instance.Culture)); ClearMarkPoints(); return; }   // the ruler is not saved
        var need = MarkIsLabel ? 1 : 2;
        if (_markPts.Count < need) { ErrorMessage = Loc.T(MarkIsLabel ? "mark_need_point" : "mark_need_two"); return; }
        if (MarkIsLabel && string.IsNullOrWhiteSpace(MarkText)) { ErrorMessage = Loc.T("mark_need_text"); return; }
        var mark = await _repo.SaveAsync(new PlanMark { GardenId = _garden.Id, Kind = MarkTool.Key, Text = MarkText.Trim(), PointsJson = JsonSerializer.Serialize(_markPts) });
        var template = new PlanMark { GardenId = mark.GardenId, Kind = mark.Kind, Text = mark.Text, PointsJson = mark.PointsJson }; var id = new[] { mark.Id };
        History.Push(new UndoEntry(Loc.F("mark_undo_add", Loc.T("mark_k_" + mark.Kind)),
            async () => { if ((await _repo.ListAsync<PlanMark>(q => q.Where(x => x.Id == id[0]))).FirstOrDefault() is { } m) await _repo.DeleteAsync(m); await LoadMarksAsync(); },
            async () => { var again = await _repo.SaveAsync(new PlanMark { GardenId = template.GardenId, Kind = template.Kind, Text = template.Text, PointsJson = template.PointsJson }); id[0] = again.Id; await LoadMarksAsync(); }));
        MarkText = ""; ClearMarkPoints(); await LoadMarksAsync();
        StatusMessage = Loc.T("mark_saved");
    }, busy: false);

    /// <summary>Right-click a note or line to delete it (can be undone).</summary>
    public Task DeleteMarkAsync(MarkShape shape) => RunAsync(async () =>
    {
        var template = new PlanMark { GardenId = shape.Mark.GardenId, Kind = shape.Mark.Kind, Text = shape.Mark.Text, PointsJson = shape.Mark.PointsJson }; var id = new[] { shape.Mark.Id };
        await _repo.DeleteAsync(shape.Mark);
        History.Push(new UndoEntry(Loc.F("mark_undo_delete", Loc.T("mark_k_" + template.Kind)),
            async () => { var again = await _repo.SaveAsync(new PlanMark { GardenId = template.GardenId, Kind = template.Kind, Text = template.Text, PointsJson = template.PointsJson }); id[0] = again.Id; await LoadMarksAsync(); },
            async () => { if ((await _repo.ListAsync<PlanMark>(q => q.Where(x => x.Id == id[0]))).FirstOrDefault() is { } m) await _repo.DeleteAsync(m); await LoadMarksAsync(); }));
        await LoadMarksAsync();
    }, busy: false);
}
