using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Cameras;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Services;
using GardenHelper.Core.Site;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record PlaceOption(string? Id, string Name) { public override string ToString() => Name; }
public sealed record CameraKindOption(string Key, string Label) { public override string ToString() => Label; }

/// <summary>Cameras on the network: add them, see the live picture, set where they are and what they watch.</summary>
public sealed partial class CamerasViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly CameraFeed _feed;
    private readonly IDialogs _dialogs;
    private readonly INavigator _nav;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private Garden? _garden;
    private bool _grabbing;

    public CamerasViewModel(Repository repo, CameraFeed feed, IDialogs dialogs, INavigator nav, ILogger<CamerasViewModel> log) : base(log)
    {
        _repo = repo; _feed = feed; _dialogs = dialogs; _nav = nav;
        Kinds = CameraUrl.Kinds.Select(k => new CameraKindOption(k, Loc.T("cam_kind_" + k))).ToList();
        _timer.Tick += async (_, _) => await GrabAsync(quiet: true);
    }

    public IReadOnlyList<CameraKindOption> Kinds { get; }
    public ObservableCollection<Camera> Cameras { get; } = new();
    public ObservableCollection<PlaceOption> Places { get; } = new();
    public int? PendingSelect { get; set; }

    [ObservableProperty] private Camera? _selected;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private CameraKindOption? _editKind;
    [ObservableProperty] private string _editUrl = "";
    [ObservableProperty] private string _editUser = "";
    [ObservableProperty] private string _editPassword = "";
    [ObservableProperty] private string _editLat = "";
    [ObservableProperty] private string _editLon = "";
    [ObservableProperty] private string _editHeading = "0";
    [ObservableProperty] private string _editFov = "90";
    [ObservableProperty] private string _editHeight = "250";
    [ObservableProperty] private string _editNotes = "";
    [ObservableProperty] private bool _editEnabled = true;
    [ObservableProperty] private PlaceOption? _editPlace;
    [ObservableProperty] private ImageSource? _frame;
    [ObservableProperty] private string _frameNote = "";
    [ObservableProperty] private bool _live;
    [ObservableProperty] private bool _hasPassword;

    partial void OnLiveChanged(bool value) { if (value && HasSelection) { _timer.Start(); _ = GrabAsync(); } else _timer.Stop(); }

    partial void OnSelectedChanged(Camera? value)
    {
        HasSelection = value is not null; Frame = null; FrameNote = ""; EditPassword = "";
        if (value is null) { Live = false; return; }
        EditName = value.Name; EditKind = Kinds.FirstOrDefault(k => k.Key == value.Kind) ?? Kinds[0]; EditUrl = value.Url; EditUser = value.Username ?? "";
        EditLat = value.Lat?.ToString("0.#####", CultureInfo.InvariantCulture) ?? ""; EditLon = value.Lon?.ToString("0.#####", CultureInfo.InvariantCulture) ?? "";
        EditHeading = value.HeadingDeg.ToString("0", CultureInfo.InvariantCulture); EditFov = value.FovDeg.ToString("0", CultureInfo.InvariantCulture);
        EditHeight = value.HeightCm.ToString("0", CultureInfo.InvariantCulture); EditNotes = value.Notes ?? ""; EditEnabled = value.Enabled;
        EditPlace = Places.FirstOrDefault(p => p.Id == value.FeatureId?.ToString()) ?? Places[0];
        HasPassword = value.SecretProtected is not null;
        if (Live) _ = GrabAsync();
    }

    public override Task LoadAsync() => RunAsync(async () =>
    {
        _garden = await _repo.ActiveGardenAsync();
        Places.Clear(); Places.Add(new PlaceOption(null, Loc.T("none")));
        if (_garden is not null)
            foreach (var f in await _repo.ListAsync<PropertyFeature>(q => q.Where(x => x.GardenId == _garden.Id).OrderBy(x => x.Name))) Places.Add(new PlaceOption(f.Id.ToString(), f.Name));
        Cameras.Clear();
        foreach (var c in await _repo.ListAsync<Camera>(q => q.OrderBy(x => x.Name))) Cameras.Add(c);
        Selected = PendingSelect is { } id ? Cameras.FirstOrDefault(c => c.Id == id) : Cameras.FirstOrDefault();
        if (PendingSelect is not null) { Live = true; PendingSelect = null; }     // opened from the plan: show the feed straight away
    });

    /// <summary>Stops polling the camera (called when the page is left).</summary>
    public void Stop() { _timer.Stop(); }
    public void Resume() { if (Live && HasSelection) _timer.Start(); }

    [RelayCommand]
    private Task AddAsync() => RunAsync(async () =>
    {
        if (_garden is null) { _garden = await _repo.SaveAsync(new Garden { Name = Loc.T("site_default_name") }); }
        var cam = await _repo.SaveAsync(new Camera { GardenId = _garden.Id, Name = $"{Loc.T("cam_default_name")} {Cameras.Count + 1}" });
        Cameras.Add(cam); Selected = cam;
        StatusMessage = Loc.T("cam_added");
    }, busy: false);

    private (Camera? Cam, string? Problem) ReadForm()
    {
        if (Selected is null) return (null, null);
        static bool N(string s, out double v) => double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        var kind = EditKind?.Key ?? "snapshot";
        if (!string.IsNullOrWhiteSpace(EditUrl) && CameraUrl.Check(kind, EditUrl, out _) != CameraUrlProblem.None) return (null, Loc.F(kind == "rtsp" ? "cam_bad_url_rtsp" : "cam_bad_url", kind));
        double? lat = null, lon = null;
        if (!string.IsNullOrWhiteSpace(EditLat) || !string.IsNullOrWhiteSpace(EditLon))
        {
            if (!N(EditLat, out var la) || !N(EditLon, out var lo) || la is < -90 or > 90 || lo is < -180 or > 180) return (null, Loc.T("site_bad_coords"));
            lat = la; lon = lo;
        }
        if (!N(EditHeading, out var heading) || !N(EditFov, out var fov) || !N(EditHeight, out var height) || fov is < 10 or > 360 || height < 0) return (null, Loc.T("cam_bad_numbers"));
        var c = new Camera
        {
            Id = Selected.Id, GardenId = Selected.GardenId, Name = EditName.Trim(), Kind = kind, Url = EditUrl.Trim(),
            Username = string.IsNullOrWhiteSpace(EditUser) ? null : EditUser.Trim(),
            SecretProtected = string.IsNullOrEmpty(EditPassword) ? Selected.SecretProtected : CameraSecrets.Protect(EditPassword),
            Lat = lat, Lon = lon, PlanX = Selected.PlanX, PlanY = Selected.PlanY, HeadingDeg = ((heading % 360) + 360) % 360, FovDeg = fov, HeightCm = height,
            FeatureId = int.TryParse(EditPlace?.Id, out var fid) ? fid : null, Enabled = EditEnabled, Notes = string.IsNullOrWhiteSpace(EditNotes) ? null : EditNotes,
        };
        return string.IsNullOrWhiteSpace(c.Name) ? (null, Loc.T("cam_name_required")) : (c, null);
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        var (cam, problem) = ReadForm();
        if (problem is not null) { ErrorMessage = problem; return; }
        if (cam is null) return;
        var saved = await _repo.SaveAsync(cam);
        var i = Cameras.IndexOf(Cameras.First(c => c.Id == saved.Id));
        Cameras[i] = saved; Selected = saved;
        StatusMessage = Loc.T("saved");
    }, busy: false);

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (Selected is null || !_dialogs.Confirm(Loc.F("cam_delete_confirm", Selected.Name))) return;
        await _repo.DeleteAsync(Selected);
        Cameras.Remove(Selected); Selected = Cameras.FirstOrDefault();
    }, busy: false);

    /// <summary>Takes a picture with the settings as typed (before saving), so a new camera can be tried straight away.</summary>
    [RelayCommand]
    private Task TestAsync() => GrabAsync();

    private async Task GrabAsync(bool quiet = false)
    {
        if (_grabbing) return;
        var (cam, problem) = ReadForm();
        if (problem is not null) { if (!quiet) ErrorMessage = problem; return; }
        if (cam is null) return;
        if (string.IsNullOrWhiteSpace(cam.Url)) { FrameNote = Loc.T("cam_need_address"); Live = false; return; }     // a new camera: guide, do not scold
        _grabbing = true;
        try
        {
            ErrorMessage = null;
            Frame = await _feed.GrabAsync(cam, string.IsNullOrEmpty(EditPassword) ? null : EditPassword);
            FrameNote = Loc.F("cam_frame_at", DateTime.Now.ToString("T", Loc.Instance.Culture));
        }
        catch (CameraException ex) { FrameNote = ex.Message; if (!quiet) ErrorMessage = ex.Message; Live = false; }
        finally { _grabbing = false; }
    }

    [RelayCommand]
    private void UseProperty()
    {
        var pts = GeoMath.FromJson(_garden?.BoundaryJson);
        GeoPoint? c = pts.Count > 0 ? GeoMath.Centroid(pts) : _garden?.Latitude is { } la && _garden.Longitude is { } lo ? new GeoPoint(la, lo) : null;
        if (c is null) { ErrorMessage = Loc.T("cam_no_property"); return; }
        EditLat = c.Lat.ToString("0.#####", CultureInfo.InvariantCulture); EditLon = c.Lon.ToString("0.#####", CultureInfo.InvariantCulture);
        StatusMessage = Loc.T("cam_use_property_done");
    }

    [RelayCommand] private void ShowOnPlan() => _nav.GoTo<DesignerViewModel>();
}
