using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Cameras;
using GardenHelper.Core.Design;
using GardenHelper.Core.Site;

namespace GardenHelper.App.ViewModels;

/// <summary>A camera icon on the garden plan, with the wedge showing what it sees. Dragging the handle at the tip of the wedge turns the camera.</summary>
public sealed partial class CameraMarker : ObservableObject
{
    public const double ViewRadiusCm = 900;

    public CameraMarker(Camera camera, double x, double y) { Camera = camera; X = x; Y = y; _heading = camera.HeadingDeg; Rebuild(); }
    public Camera Camera { get; }
    public int Id => Camera.Id;
    public string Name => Camera.Name;
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private PointCollection _view = new();
    [ObservableProperty] private double _handleX;
    [ObservableProperty] private double _handleY;
    [ObservableProperty] private double _heading;
    [ObservableProperty] private ImageSource? _preview;
    [ObservableProperty] private string _previewNote = Loc.T("cam_hover_loading");
    public string Tip => $"{Camera.Name}\n{Loc.T("cam_hover_open")}";
    internal DateTime LastPreview { get; set; }

    /// <summary>Points the camera: 0 = north = up on the plan, clockwise.</summary>
    public void SetHeading(double degrees)
    {
        Heading = ((degrees % 360) + 360) % 360;
        Rebuild();
    }

    private void Rebuild()
    {
        var fov = Camera.FovDeg;
        var pts = new PointCollection { new Point(0, 0) };
        for (var i = 0; i <= 12; i++)
        {
            var a = (Heading - fov / 2 + fov * i / 12) * Math.PI / 180;
            pts.Add(new Point(Math.Sin(a) * ViewRadiusCm, -Math.Cos(a) * ViewRadiusCm));
        }
        pts.Freeze(); View = pts;
        var h = Heading * Math.PI / 180;
        HandleX = Math.Sin(h) * ViewRadiusCm * 0.9; HandleY = -Math.Cos(h) * ViewRadiusCm * 0.9;
    }
}

public sealed partial class DesignerViewModel
{
    public ObservableCollection<CameraMarker> CameraMarkers { get; } = new();
    private CameraFeed? _camFeed;

    /// <summary>Puts every camera that has a position on the plan. A map position wins when the plan has one; otherwise the camera's own plan position is used.</summary>
    private async Task LoadCameraMarkersAsync()
    {
        CameraMarkers.Clear();
        if (_garden is null) return;
        var cams = await _repo.ListAsync<Camera>(q => q.Where(c => c.GardenId == _garden.Id && c.Enabled));
        foreach (var c in cams)
        {
            Pt? at = _frame is not null && c.Lat is { } la && c.Lon is { } lo ? _frame.ToPlan(new GeoPoint(la, lo))
                   : c.PlanX is { } px && c.PlanY is { } py ? new Pt(px, py) : null;
            if (at is not { } p) continue;
            CameraMarkers.Add(new CameraMarker(c, p.X, p.Y));
        }
    }

    /// <summary>Hover preview: one fresh picture, at most one request every 5 seconds per camera.</summary>
    public async Task LoadPreviewAsync(CameraMarker m)
    {
        if (_camFeed is null || (DateTime.UtcNow - m.LastPreview).TotalSeconds < 5) return;
        m.LastPreview = DateTime.UtcNow;
        try { m.Preview = await _camFeed.GrabAsync(m.Camera); m.PreviewNote = DateTime.Now.ToString("T", Loc.Instance.Culture); }
        catch (CameraException ex) { m.Preview = null; m.PreviewNote = ex.Message; }
    }

    /// <summary>Clicking the icon opens the Cameras tab on this camera with its feed running and its settings beside it.</summary>
    public void OpenCamera(CameraMarker m) => _nav.GoTo<CamerasViewModel>(vm => vm.PendingSelect = m.Id);

    public async Task TurnCameraAsync(CameraMarker m)
    {
        m.Camera.HeadingDeg = Math.Round(m.Heading);
        await _repo.SaveAsync(m.Camera);
        StatusMessage = Loc.F("cam_turned", m.Camera.HeadingDeg.ToString("0"));
    }

    public async Task MoveCameraAsync(CameraMarker m, double x, double y)
    {
        m.X = x; m.Y = y;
        m.Camera.PlanX = x; m.Camera.PlanY = y;
        if (_frame is not null) { var g = _frame.ToGeo(new Pt(x, y)); m.Camera.Lat = g.Lat; m.Camera.Lon = g.Lon; }
        await _repo.SaveAsync(m.Camera);
        StatusMessage = Loc.T("cam_moved");
    }

    /// <summary>Adds a camera in the middle of the plan and opens its page (live view and settings) so the address can be filled in. Works with or without a map position.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task AddCameraHereAsync() => RunAsync(async () =>
    {
        if (_garden is null) return;
        var centre = _planAll.Count > 0 ? new Pt(_planAll.Average(p => p.X), _planAll.Average(p => p.Y)) : new Pt(CanvasWidth / 2, CanvasHeight / 2);
        var cam = new Camera { GardenId = _garden.Id, Name = $"{Loc.T("cam_default_name")} {CameraMarkers.Count + 1}", PlanX = centre.X, PlanY = centre.Y };
        if (_frame is not null) { var g = _frame.ToGeo(centre); cam.Lat = g.Lat; cam.Lon = g.Lon; }
        cam = await _repo.SaveAsync(cam);
        _nav.GoTo<CamerasViewModel>(vm => vm.PendingSelect = cam.Id);
        StatusMessage = Loc.T("cam_added_plan");
    }, busy: false);
}
