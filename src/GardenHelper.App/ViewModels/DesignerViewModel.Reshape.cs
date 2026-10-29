using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Cameras;
using GardenHelper.Core.Design;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Site;

namespace GardenHelper.App.ViewModels;

/// <summary>A corner (or the "add a corner here" handle between two corners) of a traced place being reshaped.</summary>
public sealed partial class EditPin : ObservableObject
{
    public EditPin(double x, double y, bool isMid, int index) { _x = x; _y = y; IsMid = isMid; Index = index; }
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    public bool IsMid { get; }
    public int Index { get; }
}

// Select a traced place on the plan, drag its corners, add or remove corners, rename it, change what it is, or delete it.
public sealed partial class DesignerViewModel
{
    private List<Pt> _editPts = new();
    private int _editFeatureId;
    public ObservableCollection<EditPin> EditPins { get; } = new();
    [ObservableProperty] private bool _editing;
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private KindOption? _editKind;
    [ObservableProperty] private PointCollection _editShape = new();
    [ObservableProperty] private bool _editIsPlan;

    public void BeginEdit(FeatureShape s)
    {
        CancelTrace(); Selected = null;
        _editFeatureId = s.Id; _editPts = s.Plan.Select(p => new Pt(p.X, p.Y)).ToList();
        EditName = s.Name; EditKind = TraceKinds.FirstOrDefault(k => k.Key == s.Kind); EditIsPlan = s.IsStructure || s.Kind == "greenhouse";
        Editing = true; RebuildEditPins();
    }

    private void RebuildEditPins()
    {
        EditPins.Clear();
        for (var i = 0; i < _editPts.Count; i++) EditPins.Add(new EditPin(_editPts[i].X, _editPts[i].Y, false, i));
        for (var i = 0; i < _editPts.Count; i++)
        {
            var a = _editPts[i]; var b = _editPts[(i + 1) % _editPts.Count];
            EditPins.Add(new EditPin((a.X + b.X) / 2, (a.Y + b.Y) / 2, true, i));
        }
        EditShape = new PointCollection(_editPts.Select(p => new Point(p.X, p.Y)));
    }

    /// <summary>A corner is being dragged: the outline follows it live.</summary>
    public void MoveEditPin(EditPin pin)
    {
        if (pin.IsMid || pin.Index >= _editPts.Count) return;
        _editPts[pin.Index] = new Pt(pin.X, pin.Y);
        EditShape = new PointCollection(_editPts.Select(p => new Point(p.X, p.Y)));
    }

    /// <summary>The handle between two corners was clicked: a new corner appears there.</summary>
    public async Task AddCornerAsync(EditPin mid)
    {
        if (!mid.IsMid || _editPts.Count >= 40) return;
        _editPts.Insert(mid.Index + 1, new Pt(mid.X, mid.Y));
        RebuildEditPins(); await CommitEditAsync();
    }

    public async Task RemoveCornerAsync(EditPin pin)
    {
        if (pin.IsMid || _editPts.Count <= GeoMath.MinBoundaryPins) { ErrorMessage = Loc.T("edit_min_corners"); return; }
        _editPts.RemoveAt(pin.Index); RebuildEditPins(); await CommitEditAsync();
    }

    /// <summary>Saves the shape (and name and kind) of the place being edited. Everything else on the plan stays exactly where it is on the ground.</summary>
    public Task CommitEditAsync() => RunAsync(async () =>
    {
        if (_garden is null || _frame is null || _features.FirstOrDefault(f => f.Id == _editFeatureId) is not { } feature) return;
        var geo = _editPts.Select(p => _frame.ToGeo(p)).ToList();
        if (GeoMath.SelfIntersects(geo)) { ErrorMessage = Loc.T("trace_crossing"); await ReloadKeepingViewAsync(null); ReselectEdit(); return; }
        var g0 = _frame.ToGeo(new Pt(0, 0));
        feature.PointsJson = GeoMath.ToJson(geo);
        if (!string.IsNullOrWhiteSpace(EditName)) feature.Name = EditName.Trim();
        if (EditKind is not null) feature.Kind = EditKind.Key;
        await _repo.SaveAsync(feature);
        await ReloadKeepingViewAsync(g0);
        ReselectEdit();
    }, busy: false);

    private void ReselectEdit() { if (Editing && FeatureShapes.FirstOrDefault(f => f.Id == _editFeatureId) is { } s) { _editPts = s.Plan.Select(p => new Pt(p.X, p.Y)).ToList(); RebuildEditPins(); } else if (Editing) Editing = false; }

    [RelayCommand] private Task SaveEditAsync() => CommitEditAsync();
    [RelayCommand] private void DoneEdit() { Editing = false; EditPins.Clear(); }
    [RelayCommand] private void OpenEditPlan() { var id = _editFeatureId; DoneEdit(); _nav.GoTo<InteriorViewModel>(vm => vm.FeatureId = id); }

    [RelayCommand]
    private Task DeleteEditAsync() => RunAsync(async () =>
    {
        if (_features.FirstOrDefault(f => f.Id == _editFeatureId) is not { } feature || !_dialogs.Confirm(Loc.F("edit_delete_confirm", feature.Name))) return;
        var g0 = _frame?.ToGeo(new Pt(0, 0));
        await _repo.DeleteAsync(feature);
        DoneEdit(); await ReloadKeepingViewAsync(g0);
    }, busy: false);

    /// <summary>
    /// Reloads the plan after the places changed. The plan's frame is derived from all places, so adding or reshaping one can move its origin:
    /// everything drawn on the plan (beds, cameras) is shifted by the same amount so it stays where it is on the ground.
    /// </summary>
    private async Task ReloadKeepingViewAsync(GeoPoint? anchor)
    {
        var keepScale = Scale;
        await LoadAsync();          // LoadAsync realigns beds and cameras to the ground anchor, whatever changed
        Scale = keepScale;
    }

    /// <summary>
    /// The plan's frame is derived from all places, so adding, reshaping or deleting one (here or on the map page) can move its origin.
    /// The garden remembers which spot on the ground was at the plan's corner; if that spot has moved, every bed and camera is shifted by the same amount
    /// so each one stays where it is on the ground.
    /// </summary>
    private async Task AlignToAnchorAsync(List<GardenBed> beds)
    {
        if (_frame is null || _garden is null) return;
        var here = _frame.ToGeo(new Pt(0, 0));
        if (_garden.PlanAnchorLat is { } la && _garden.PlanAnchorLon is { } lo)
        {
            var p = _frame.ToPlan(new GeoPoint(la, lo));
            if (Math.Abs(p.X) > 0.5 || Math.Abs(p.Y) > 0.5)
            {
                foreach (var b in beds) { b.X += p.X; b.Y += p.Y; await _repo.SaveAsync(b); }
                foreach (var c in await _repo.ListAsync<Camera>(q => q.Where(x => x.PlanX != null))) { c.PlanX += p.X; c.PlanY += p.Y; await _repo.SaveAsync(c); }
            }
            if (Math.Abs(p.X) <= 0.5 && Math.Abs(p.Y) <= 0.5 && Math.Abs(la - here.Lat) < 1e-9 && Math.Abs(lo - here.Lon) < 1e-9) return;
        }
        _garden.PlanAnchorLat = here.Lat; _garden.PlanAnchorLon = here.Lon;
        _garden = await _repo.SaveAsync(_garden);
    }
}
