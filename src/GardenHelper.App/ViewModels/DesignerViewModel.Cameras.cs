using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Cameras;
using GardenHelper.Core.Design;
using GardenHelper.Core.Site;

namespace GardenHelper.App.ViewModels;

/// <summary>A camera icon on the garden plan, with the wedge showing what it sees.</summary>
public sealed partial class CameraMarker : ObservableObject
{
    public CameraMarker(Camera camera, double x, double y, PointCollection view) { Camera = camera; X = x; Y = y; View = view; }
    public Camera Camera { get; }
    public int Id => Camera.Id;
    public string Name => Camera.Name;
    public PointCollection View { get; }
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private ImageSource? _preview;
    [ObservableProperty] private string _previewNote = Loc.T("cam_hover_loading");
    public string Tip => $"{Camera.Name}\n{Loc.T("cam_hover_open")}";
    internal DateTime LastPreview { get; set; }
}

public sealed partial class DesignerViewModel
{
    private const double CameraViewRadiusCm = 900;
    public ObservableCollection<CameraMarker> CameraMarkers { get; } = new();
    private CameraFeed? _camFeed;

    /// <summary>Puts every camera that has a position on the plan (cameras without one wait in the Cameras tab).</summary>
    private async Task LoadCameraMarkersAsync()
    {
        CameraMarkers.Clear();
        if (_garden is null || _frame is null) return;
        var cams = await _repo.ListAsync<Camera>(q => q.Where(c => c.GardenId == _garden.Id && c.Enabled));
        foreach (var c in cams.Where(c => c.Lat is not null && c.Lon is not null))
        {
            var p = _frame.ToPlan(new GeoPoint(c.Lat!.Value, c.Lon!.Value));
            // Wedge: apex at the camera, opening FovDeg wide, centred on the heading (0 = north = up on the plan).
            var pts = new PointCollection { new Point(0, 0) };
            for (var i = 0; i <= 12; i++)
            {
                var a = (c.HeadingDeg - c.FovDeg / 2 + c.FovDeg * i / 12) * Math.PI / 180;
                pts.Add(new Point(Math.Sin(a) * CameraViewRadiusCm, -Math.Cos(a) * CameraViewRadiusCm));
            }
            pts.Freeze();
            CameraMarkers.Add(new CameraMarker(c, p.X, p.Y, pts));
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

    public async Task MoveCameraAsync(CameraMarker m, double x, double y)
    {
        if (_frame is null) return;
        m.X = x; m.Y = y;
        var g = _frame.ToGeo(new Pt(x, y));
        m.Camera.Lat = g.Lat; m.Camera.Lon = g.Lon;
        await _repo.SaveAsync(m.Camera);
        StatusMessage = Loc.T("cam_moved");
    }

    /// <summary>Adds a camera in the middle of what is visible and opens it so the address can be filled in.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task AddCameraHereAsync() => RunAsync(async () =>
    {
        if (_garden is null || _frame is null) { ErrorMessage = Loc.T("design_need_coords"); return; }
        var g = _frame.ToGeo(_planAll.Count > 0 ? new Pt(_planAll.Average(p => p.X), _planAll.Average(p => p.Y)) : new Pt(CanvasWidth / 2, CanvasHeight / 2));
        var cam = await _repo.SaveAsync(new Camera { GardenId = _garden.Id, Name = Loc.T("cam_default_name"), Lat = g.Lat, Lon = g.Lon });
        _nav.GoTo<CamerasViewModel>(vm => vm.PendingSelect = cam.Id);
    }, busy: false);
}
