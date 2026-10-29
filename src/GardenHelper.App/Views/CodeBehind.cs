using GardenHelper.Core.Library.Reading;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using GardenHelper.Core.Site;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using GardenHelper.App.Infrastructure;
using GardenHelper.App.ViewModels;
using GardenHelper.Core.Data;
using GardenHelper.Core.Library;
using Microsoft.Extensions.DependencyInjection;

namespace GardenHelper.App.Views;

public partial class DashboardView : UserControl { public DashboardView() => InitializeComponent(); }
public partial class TasksView : UserControl { public TasksView() => InitializeComponent(); }
public partial class PlantsView : UserControl { public PlantsView() => InitializeComponent(); }
public partial class SeedsView : UserControl { public SeedsView() => InitializeComponent(); }
public partial class JournalView : UserControl { public JournalView() => InitializeComponent(); }
public partial class GardenView : UserControl { public GardenView() => InitializeComponent(); }
public partial class SearchView : UserControl { public SearchView() => InitializeComponent(); }
public partial class SettingsView : UserControl { public SettingsView() => InitializeComponent(); }
public partial class AboutView : UserControl { public AboutView() => InitializeComponent(); }

public partial class CamerasView : UserControl
{
    public CamerasView() => InitializeComponent();
    private void OnLoaded(object sender, RoutedEventArgs e) => (DataContext as ViewModels.CamerasViewModel)?.Resume();
    private void OnUnloaded(object sender, RoutedEventArgs e) => (DataContext as ViewModels.CamerasViewModel)?.Stop();
}
public partial class AnimalsView : UserControl { public AnimalsView() => InitializeComponent(); }
public partial class ComingUpView : UserControl { public ComingUpView() => InitializeComponent(); }
public partial class BudgetView : UserControl { public BudgetView() => InitializeComponent(); }
public partial class InventoryView : UserControl { public InventoryView() => InitializeComponent(); }
public partial class GreenhouseView : UserControl
{
    private double _dx, _dy, _sx, _sy;
    public GreenhouseView() => InitializeComponent();
    private ViewModels.GreenhouseViewModel? Vm => DataContext as ViewModels.GreenhouseViewModel;
    private static ViewModels.LayoutItem? Item(object sender) => (sender as FrameworkElement)?.DataContext as ViewModels.LayoutItem;
    private void OnLoaded(object sender, RoutedEventArgs e) => Vm?.Resume();
    private void OnUnloaded(object sender, RoutedEventArgs e) => Vm?.Stop();
    private void OnItemDragStart(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        if (Item(sender) is not { } li || Vm is not { } vm) return;
        vm.SelectedItem = li; _sx = li.X; _sy = li.Y; _dx = _dy = 0;
    }
    private void OnItemDrag(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (Item(sender) is not { } li) return;
        _dx += e.HorizontalChange; _dy += e.VerticalChange; li.X = _sx + _dx; li.Y = _sy + _dy;
    }
    private async void OnItemDragDone(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (Item(sender) is { } li && Vm is { } vm) await vm.ItemMovedAsync(li);
    }
}
public partial class PlannerView : UserControl
{
    public PlannerView() => InitializeComponent();
    private void OnPrint(object sender, RoutedEventArgs e) => Printing.PrintPicture(Page, Loc.T("nav_planner"), (DataContext as ViewModels.PlannerViewModel)?.Year.ToString());
}
public partial class SeasonView : UserControl
{
    public SeasonView() => InitializeComponent();
    private void OnPrint(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.SeasonViewModel vm) return;
        var (title, tiles, sections) = vm.Report;
        Printing.PrintDocument(Printing.ReportDocument(title, tiles.Select(t => (t.Value, t.Label)), sections.Select(s => (s.Title, (IEnumerable<string>)s.Lines))), title);
    }
}
public partial class PhotosView : UserControl { public PhotosView() => InitializeComponent(); }
public partial class WelcomeView : UserControl { public WelcomeView() => InitializeComponent(); }
public partial class PiGuideView : UserControl
{
    public PiGuideView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ViewModels.PiGuideViewModel vm) return;
            void Show() => Doc.Document = vm.Document;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ViewModels.PiGuideViewModel.Document)) Show(); };
            Show();
        };
    }
}
public partial class TodayView : UserControl
{
    public TodayView() => InitializeComponent();
    private void OnPrint(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.TodayViewModel vm) return;
        var title = Loc.T("today_print_title") + " · " + DateTime.Today.ToString("d MMMM yyyy", Loc.Instance.Culture);
        var tiles = new[] { (vm.UrgentCount.ToString(), Loc.T("today_t_urgent")), (vm.OpenCount.ToString(), Loc.T("today_t_open")), (vm.WaterCount.ToString(), Loc.T("today_t_water")), (vm.RipeningCount.ToString(), Loc.T("today_t_ready")) };
        var sections = vm.Groups.Select(g => (g.Title, (IEnumerable<string>)g.Rows.Where(r => !r.IsDone).Select(r => (r.IsUrgent ? "! " : "") + r.Title).ToList())).Where(s => s.Item2.Any()).ToList();
        if (vm.Ripenings.Count > 0) sections.Add((Loc.T("today_ripening"), vm.Ripenings.Select(r => $"{r.Title}: {r.When}").ToList()));
        Printing.PrintDocument(Printing.ReportDocument(title, tiles, sections), title);
    }
}
public partial class ConnectView : UserControl { public ConnectView() => InitializeComponent(); }
public partial class SiteView : UserControl { public SiteView() => InitializeComponent(); }
public partial class SpaceView : UserControl { public SpaceView() => InitializeComponent(); }

public partial class PropertyView : UserControl
{
    public PropertyView() => InitializeComponent();
    private void OnLabelSize(object sender, SizeChangedEventArgs e) => LabelPlacement.Centre(sender, e);

    private void OnShapeClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PlanShape shape && DataContext is PropertyViewModel vm) vm.OpenPlace(shape.FeatureId);
        e.Handled = true;
    }
}

public partial class MapView : UserControl
{
    private MapViewModel? _vm;
    private int _dragPin = -1;
    private bool _down, _panning, _moved;
    private Point _start, _last;
    private int _generation;

    public MapView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.MapChanged -= Render;
            _vm = DataContext as MapViewModel;
            if (_vm is not null) _vm.MapChanged += Render;
            Render();
        };
        Unloaded += (_, _) => { if (_vm is not null) _vm.MapChanged -= Render; };
    }

    // World-pixel position of the top-left corner of the viewport.
    private (double X, double Y) TopLeft()
    {
        var (cx, cy) = GeoMath.ToWorldPixel(new GeoPoint(_vm!.CenterLat, _vm.CenterLon), _vm.Zoom);
        return (cx - Surface.ActualWidth / 2, cy - Surface.ActualHeight / 2);
    }

    private GeoPoint ScreenToGeo(Point p) { var tl = TopLeft(); return GeoMath.FromWorldPixel(tl.X + p.X, tl.Y + p.Y, _vm!.Zoom); }

    private Point GeoToScreen(GeoPoint g)
    {
        var tl = TopLeft(); var (x, y) = GeoMath.ToWorldPixel(g, _vm!.Zoom);
        return new Point(x - tl.X, y - tl.Y);
    }

    private void Render()
    {
        if (_vm is null || Surface.ActualWidth < 10 || Surface.ActualHeight < 10) return;
        TileLayer.Children.Clear(); Overlay.Children.Clear();
        var z = _vm.Zoom; var tl = TopLeft(); var max = 1 << z;
        var x0 = (int)Math.Floor(tl.X / 256); var x1 = (int)Math.Floor((tl.X + Surface.ActualWidth) / 256);
        var y0 = (int)Math.Floor(tl.Y / 256); var y1 = (int)Math.Floor((tl.Y + Surface.ActualHeight) / 256);
        for (var ty = Math.Max(0, y0); ty <= Math.Min(max - 1, y1); ty++)
            for (var tx = Math.Max(0, x0); tx <= Math.Min(max - 1, x1); tx++)
            {
                var img = _vm.TryGetCachedTile(z, tx, ty);
                if (img is null) _vm.RequestTile(z, tx, ty);
                var el = img is null
                    ? (FrameworkElement)new System.Windows.Shapes.Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0x2B, 0x38, 0x2B)), Width = 256, Height = 256 }
                    : new Image { Source = img, Width = 256, Height = 256 };
                Canvas.SetLeft(el, tx * 256 - tl.X); Canvas.SetTop(el, ty * 256 - tl.Y);
                TileLayer.Children.Add(el);
            }

        // Every saved shape, coloured by kind and labelled with its name; the one being edited is drawn bold with its pins.
        foreach (var shape in _vm.Shapes)
        {
            var sp = shape.Points.Select(GeoToScreen).ToList();
            if (sp.Count < 2) continue;
            var col = FeatureColors.For(shape.Kind);
            var selected = ReferenceEquals(shape, _vm.Selected);
            var stroke = new SolidColorBrush(selected ? Color.FromRgb(0xE5, 0x8C, 0x62) : col);
            if (sp.Count >= 3)
                Overlay.Children.Add(new System.Windows.Shapes.Polygon
                {
                    Points = new PointCollection(sp), Stroke = stroke, StrokeThickness = selected ? 4 : 2, StrokeLineJoin = PenLineJoin.Round,
                    StrokeDashArray = shape.IsOutline ? new DoubleCollection { 4, 2 } : null,
                    Fill = new SolidColorBrush(Color.FromArgb(shape.IsOutline ? (byte)15 : (byte)(selected ? 85 : 70), col.R, col.G, col.B)),
                });
            else Overlay.Children.Add(new System.Windows.Shapes.Polyline { Points = new PointCollection(sp), Stroke = stroke, StrokeThickness = 3 });
            if (sp.Count >= 3 && shape.Name.Length > 0)
            {
                var label = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1),
                    Child = new TextBlock { Text = shape.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x2A, 0x1C)) },
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, sp.Average(p => p.X) - label.DesiredSize.Width / 2); Canvas.SetTop(label, sp.Average(p => p.Y) - label.DesiredSize.Height / 2);
                Overlay.Children.Add(label);
            }
        }

        var pts = _vm.Points.Select(GeoToScreen).ToList();
        for (var i = 0; i < pts.Count; i++)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush(Color.FromRgb(0xC0, 0x56, 0x2B)), Stroke = Brushes.White, StrokeThickness = 2 };
            Canvas.SetLeft(dot, pts[i].X - 11); Canvas.SetTop(dot, pts[i].Y - 11);
            var num = new TextBlock { Text = (i + 1).ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 11, Width = 22, TextAlignment = TextAlignment.Center };
            Canvas.SetLeft(num, pts[i].X - 11); Canvas.SetTop(num, pts[i].Y - 8);
            Overlay.Children.Add(dot); Overlay.Children.Add(num);
        }
    }

    private int HitPin(Point p)
    {
        if (_vm is null) return -1;
        var pts = _vm.Points.Select(GeoToScreen).ToList();
        for (var i = pts.Count - 1; i >= 0; i--)
            if ((pts[i] - p).Length <= 13) return i;
        return -1;
    }

    private void OnSize(object sender, SizeChangedEventArgs e) => Render();

    private void OnDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_vm is null || !_vm.MapEnabled) return;
        Surface.Focus(); Surface.CaptureMouse();
        _start = _last = e.GetPosition(Surface); _down = true; _moved = false; _panning = false;
        _dragPin = HitPin(_start);
    }

    private void OnMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_vm is null) return;
        var p = e.GetPosition(Surface);
        var g = ScreenToGeo(p);
        _vm.CursorText = $"{g.Lat:0.00000}, {g.Lon:0.00000}";
        if (!_down) return;
        if (!_moved && (p - _start).Length > 4) { _moved = true; _panning = _dragPin < 0; }
        if (!_moved) return;
        if (_dragPin >= 0) _vm.MovePin(_dragPin, g);
        else if (_panning)
        {
            var (cx, cy) = GeoMath.ToWorldPixel(new GeoPoint(_vm.CenterLat, _vm.CenterLon), _vm.Zoom);
            var c = GeoMath.FromWorldPixel(cx - (p.X - _last.X), cy - (p.Y - _last.Y), _vm.Zoom);
            _vm.CenterLat = c.Lat; _vm.CenterLon = c.Lon;
        }
        _last = p;
    }

    private void OnUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_down || _vm is null) return;
        _down = false; Surface.ReleaseMouseCapture();
        if (!_moved) _vm.AddPin(ScreenToGeo(e.GetPosition(Surface)));      // a plain click drops a pin
        _dragPin = -1; _panning = false;
    }

    private void OnRightDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_vm is null || !_vm.MapEnabled) return;
        var i = HitPin(e.GetPosition(Surface));
        if (i >= 0) _vm.RemovePinAt(i);
    }

    private void OnWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (_vm is null || !_vm.MapEnabled) return;
        var p = e.GetPosition(Surface);
        var under = ScreenToGeo(p);
        var z = Math.Clamp(_vm.Zoom + (e.Delta > 0 ? 1 : -1), 3, 19);
        if (z == _vm.Zoom) return;
        // Keep the point under the cursor fixed while zooming.
        var (wx, wy) = GeoMath.ToWorldPixel(under, z);
        var c = GeoMath.FromWorldPixel(wx - (p.X - Surface.ActualWidth / 2), wy - (p.Y - Surface.ActualHeight / 2), z);
        _vm.CenterLat = c.Lat; _vm.CenterLon = c.Lon; _vm.Zoom = z;
        e.Handled = true;
    }
}
public partial class MonitorView : UserControl { public MonitorView() => InitializeComponent(); }

public partial class DesignerView : UserControl
{
    // Raw (unsnapped) position while dragging, so snapping never swallows small movements.
    private double _rawA, _rawB;

    public DesignerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookViewModel();
    }
    private void OnLabelSize(object sender, SizeChangedEventArgs e) => LabelPlacement.Centre(sender, e);

    private void OnPaletteItem(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PaletteItem item) Vm?.AddElementCommand.Execute(item);
    }

    private double _turnX, _turnY;
    private void OnCamTurnStart(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) { if (Cam(sender) is { } m) { _turnX = m.HandleX; _turnY = m.HandleY; } }
    private void OnCamTurn(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (Cam(sender) is not { } m) return;
        _turnX += e.HorizontalChange; _turnY += e.VerticalChange;
        var deg = Math.Atan2(_turnX, -_turnY) * 180 / Math.PI;           // angle of the pointer around the camera, 0 = up
        m.SetHeading(Math.Round(deg / 5) * 5);                            // snaps to 5 degrees
    }
    private async void OnCamTurnDone(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) { if (Cam(sender) is { } m && Vm is { } vm) await vm.TurnCameraAsync(m); }

    private double _camDx, _camDy, _camStartX, _camStartY;
    private static CameraMarker? Cam(object sender) => (sender as FrameworkElement)?.DataContext as CameraMarker;
    private void OnCameraTip(object sender, ToolTipEventArgs e) { if (Cam(sender) is { } m && Vm is { } vm) _ = vm.LoadPreviewAsync(m); }
    private void OnCameraDragStart(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) { if (Cam(sender) is { } m) { _camStartX = m.X; _camStartY = m.Y; _camDx = _camDy = 0; } }
    private void OnCameraDrag(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (Cam(sender) is not { } m) return;
        _camDx += e.HorizontalChange; _camDy += e.VerticalChange;
        if (Math.Abs(_camDx) + Math.Abs(_camDy) > 4 / Math.Max(Vm?.Scale ?? 1, 0.001)) { m.X = _camStartX + _camDx; m.Y = _camStartY + _camDy; }
    }
    private async void OnCameraDragDone(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (Cam(sender) is not { } m || Vm is not { } vm) return;
        if (Math.Abs(_camDx) + Math.Abs(_camDy) <= 4 / Math.Max(vm.Scale, 0.001)) vm.OpenCamera(m);   // a click, not a drag
        else await vm.MoveCameraAsync(m, m.X, m.Y);
    }

    private static BedItem? Bed(object sender) => (sender as FrameworkElement)?.DataContext as BedItem;
    private DesignerViewModel? Vm => DataContext as DesignerViewModel;

    private void OnBedDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Bed(sender) is not { } b || Vm is not { } vm) return;
        vm.Selected = b;
        if (e.ClickCount == 2) { vm.ZoomToSelectedCommand.Execute(null); e.Handled = true; return; }   // double-click an element to zoom in on it
        if (e.ClickCount == 1 && sender is FrameworkElement el) _ = vm.ProbePlantAsync(b, e.GetPosition(el));   // click on a plant: show its card
    }

    /// <summary>Hovering a plant icon names just that plant; elsewhere on the bed the tip lists the whole bed.</summary>
    private void OnBedHover(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (Bed(sender) is not { } b || Vm is not { } vm || sender is not FrameworkElement el) return;
        el.ToolTip = vm.PlantTipAt(b, e.GetPosition(el)) ?? b.TipText;
    }

    private void OnBadgeClick(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is FeatureBadge badge) Vm?.OpenBadgeCommand.Execute(badge); }

    private void OnMoveStart(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        if (Bed(sender) is { } b) { _rawA = b.X; _rawB = b.Y; if (Vm is { } vm) vm.Selected = b; }
    }

    private void OnMove(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (Bed(sender) is not { } b || Vm is not { } vm) return; if (b.Locked) { vm.StatusMessage = Loc.T("design_locked_msg"); return; }
        // The thumb sits inside the bed's rotation, so turn the pointer movement back into garden axes.
        var r = b.Rotation * Math.PI / 180; var cos = Math.Cos(r); var sin = Math.Sin(r);
        var dx = e.HorizontalChange * cos - e.VerticalChange * sin;
        var dy = e.HorizontalChange * sin + e.VerticalChange * cos;
        _rawA += dx; _rawB += dy;
        b.X = Math.Max(0, vm.SnapValue(_rawA));
        b.Y = Math.Max(0, vm.SnapValue(_rawB));
        vm.UpdateOverlap();
    }

    private void OnResizeStart(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        if (Bed(sender) is { } b) { _rawA = b.Width; _rawB = b.Height; }
    }

    private void OnResize(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (Bed(sender) is not { } b || Vm is not { } vm) return; if (b.Locked) { vm.StatusMessage = Loc.T("design_locked_msg"); return; }
        _rawA += e.HorizontalChange; _rawB += e.VerticalChange;
        b.Width = vm.SnapValue(_rawA);
        b.Height = vm.SnapValue(_rawB);
        vm.UpdateOverlap();
    }

    private async void OnMoveDone(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (Bed(sender) is { } b && Vm is { } vm) await vm.CommitAsync(b, Loc.F("design_undo_move", b.Name));
    }

    private async void OnResizeDone(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (Bed(sender) is { } b && Vm is { } vm) await vm.CommitAsync(b, Loc.F("design_undo_resize", b.Name));
    }

    // ---- Canvas navigation: wheel zooms at the cursor, drag the background to pan (middle button works too) ----
    private bool _panning; private Point _panStart; private double _panH, _panV;

    private void HookViewModel()
    {
        if (Vm is not { } vm) return;
        vm.ZoomRequested += factor => ZoomAt(factor, null);
        vm.CenterRequested += (x, y) => Dispatcher.BeginInvoke(new Action(() =>
        {
            CanvasScroll.UpdateLayout();
            CanvasScroll.ScrollToHorizontalOffset(Math.Max(0, x * vm.Scale - CanvasScroll.ViewportWidth / 2));
            CanvasScroll.ScrollToVerticalOffset(Math.Max(0, y * vm.Scale - CanvasScroll.ViewportHeight / 2));
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnViewportSize(object sender, SizeChangedEventArgs e)
    {
        if (Vm is { } vm) { vm.ViewportWidth = e.NewSize.Width; vm.ViewportHeight = e.NewSize.Height; vm.ViewportChanged(); }   // the real size of the plan window (scroll bars come and go and would make the numbers jump)
    }

    /// <summary>Zooms by a factor, keeping the point under the pointer (or the middle of the window) where it is.</summary>
    private void ZoomAt(double factor, Point? viewportPoint)
    {
        if (Vm is not { } vm) return;
        var vp = viewportPoint ?? new Point(CanvasScroll.ViewportWidth / 2, CanvasScroll.ViewportHeight / 2);
        var wx = (CanvasScroll.HorizontalOffset + vp.X) / vm.Scale; var wy = (CanvasScroll.VerticalOffset + vp.Y) / vm.Scale;
        vm.Scale = Math.Clamp(Math.Round(vm.Scale * factor, 4), DesignerViewModel.MinScale, DesignerViewModel.MaxScale);
        CanvasScroll.UpdateLayout();
        CanvasScroll.ScrollToHorizontalOffset(Math.Max(0, wx * vm.Scale - vp.X)); CanvasScroll.ScrollToVerticalOffset(Math.Max(0, wy * vm.Scale - vp.Y));
    }

    private void OnWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        ZoomAt(e.Delta > 0 ? 1.2 : 1 / 1.2, e.GetPosition(CanvasScroll));
        e.Handled = true;
    }

    private static bool IsInside(DependencyObject? d, DependencyObject ancestor)
    {
        while (d is not null) { if (ReferenceEquals(d, ancestor)) return true; d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d); }
        return false;
    }

    private void OnPrintPlan(object sender, RoutedEventArgs e) => Printing.PrintPicture(CanvasScroll, Loc.T("nav_designer"), Loc.T("print_plan_hint"));

    private void OnTraceDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Vm is { } vm && sender is IInputElement el) { vm.AddTracePoint(e.GetPosition(el)); e.Handled = true; }
    }

    private void OnShapeClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FeatureShape shape || Vm is not { } vm || vm.Tracing) return;
        if (e.ClickCount == 2) vm.OpenShapeCommand.Execute(shape); else vm.BeginEdit(shape);
        e.Handled = true;
    }

    private static EditPin? Pin(object sender) => (sender as FrameworkElement)?.DataContext as EditPin;
    private void OnEditPinDrag(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (Pin(sender) is not { } p || p.IsMid || Vm is not { } vm) return;
        p.X += e.HorizontalChange; p.Y += e.VerticalChange; vm.MoveEditPin(p);
    }
    private async void OnEditPinDone(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) { if (Pin(sender) is { IsMid: false } && Vm is { } vm) await vm.CommitEditAsync(); }
    private async void OnEditMidClick(object sender, RoutedEventArgs e) { if (Pin(sender) is { IsMid: true } p && Vm is { } vm) await vm.AddCornerAsync(p); }
    private async void OnEditPinRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Pin(sender) is { IsMid: false } p && Vm is { } vm) { await vm.RemoveCornerAsync(p); e.Handled = true; }
    }

    private void OnPanStart(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Vm is { Tracing: true } && e.ChangedButton == System.Windows.Input.MouseButton.Left) return;   // clicks place pins while tracing
        if (Vm is { AnimalCardOpen: true } ac) ac.AnimalCardOpen = false;
        if (Vm is { CardOpen: true } open) open.CardOpen = false;   // any click on the plan closes the plant card (clicking a plant opens it again)
        var middle = e.ChangedButton == System.Windows.Input.MouseButton.Middle;
        var background = e.ChangedButton == System.Windows.Input.MouseButton.Left && e.OriginalSource is DependencyObject src && IsInside(src, World)
                         && (src as FrameworkElement)?.DataContext is not (BedItem or FeatureShape or EditPin);
        if (!middle && !background) return;
        _panning = true; _panStart = e.GetPosition(CanvasScroll); _panH = CanvasScroll.HorizontalOffset; _panV = CanvasScroll.VerticalOffset;
        CanvasScroll.CaptureMouse(); CanvasScroll.Cursor = System.Windows.Input.Cursors.Hand; e.Handled = middle;
    }

    private void OnPanMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_panning) return;
        var p = e.GetPosition(CanvasScroll);
        CanvasScroll.ScrollToHorizontalOffset(_panH - (p.X - _panStart.X));
        CanvasScroll.ScrollToVerticalOffset(_panV - (p.Y - _panStart.Y));
    }

    private void OnPanEnd(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false; CanvasScroll.ReleaseMouseCapture(); CanvasScroll.Cursor = null;
    }
    private void OnWorldMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (Vm is not { } vm) return;
        var p = e.GetPosition(World);      // already in garden centimetres: the scale is a layout transform
        vm.CursorText = Loc.F("design_cursor", (p.X / 100).ToString("0.00", Loc.Instance.Culture), (p.Y / 100).ToString("0.00", Loc.Instance.Culture));
    }

    // ---- Drag a plant card onto a bed ----
    private void OnCardMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || (sender as FrameworkElement)?.DataContext is not PlantCard card) return;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject("garden.plant", card.Plant.Id), DragDropEffects.Copy);
    }

    private void OnBedDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("garden.plant") && Bed(sender)?.Kind is "bed" or "raised" or "greenhouse" or "tree" ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnBedDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("garden.plant") is string id && Bed(sender) is { } b && Vm is { } vm) await vm.AddPlantToBedAsync(b, id);
        e.Handled = true;
    }

    // ---- Keyboard ----
    private async void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Vm is not { } vm || e.OriginalSource is System.Windows.Controls.TextBox or System.Windows.Controls.Primitives.TextBoxBase) return;
        var ctrl = System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control;
        switch (e.Key)
        {
            case System.Windows.Input.Key.Z when ctrl: await vm.UndoCommand.ExecuteAsync(null); break;
            case System.Windows.Input.Key.Y when ctrl: await vm.RedoCommand.ExecuteAsync(null); break;
            case System.Windows.Input.Key.S when ctrl: await vm.SaveSelectedCommand.ExecuteAsync(null); break;
            case System.Windows.Input.Key.Delete: await vm.DeleteSelectedCommand.ExecuteAsync(null); break;
            case System.Windows.Input.Key.R: await vm.RotateCommand.ExecuteAsync("15"); break;
            case System.Windows.Input.Key.Add or System.Windows.Input.Key.OemPlus: vm.ZoomInCommand.Execute(null); break;
            case System.Windows.Input.Key.Subtract or System.Windows.Input.Key.OemMinus: vm.ZoomOutCommand.Execute(null); break;
            case System.Windows.Input.Key.Escape: vm.Selected = null; break;
            default: return;
        }
        e.Handled = true;
    }
}

public partial class LibraryView : UserControl
{
    public LibraryView() => InitializeComponent();

    private void OnCaseSize(object sender, SizeChangedEventArgs e) { if (e.WidthChanged && DataContext is LibraryViewModel vm) vm.SetShelfWidth(e.NewSize.Width - 20); }

    /// <summary>One click picks a book (its details open on the right); a double click opens it for reading.</summary>
    private void OnSpineDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm || (sender as FrameworkElement)?.DataContext is not SpineItem spine) return;
        vm.SelectBookCommand.Execute(spine.Row);
        if (e.ClickCount == 2) vm.ReadCommand.Execute(spine.Row);
    }

    private void OnIndexTitle(object sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryViewModel vm && (sender as FrameworkElement)?.DataContext is BookRow row) vm.SelectBookCommand.Execute(row);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm || e.Data.GetData(DataFormats.FileDrop) is not string[] dropped) return;
        var files = dropped.SelectMany(p => Directory.Exists(p)
                ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Where(f => BookFormats.For(f) is not null)
                : new[] { p })
            .ToList();
        if (files.Count > 0) await vm.ImportAsync(files);
    }

    private void OnHitSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not LibraryHit hit || DataContext is not LibraryViewModel vm) return;
        ((ListBox)sender).SelectedItem = null;
        vm.OpenHitCommand.Execute(hit);
    }
}

public partial class ReaderView : UserControl
{
    private ReaderViewModel? _vm;
    private readonly PdfPageRenderer _pdf = new();
    private int _version;
    private readonly System.Windows.Threading.DispatcherTimer _resize = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public ReaderView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        _resize.Tick += (_, _) => { _resize.Stop(); if (_vm?.Content is { Kind: ReaderKind.Pdf }) _ = RenderAsync(); };
        Unloaded += (_, _) => { if (_vm is not null) _vm.PropertyChanged -= OnVmChanged; _resize.Stop(); };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as ReaderViewModel;
        if (_vm is null) return;
        _vm.PropertyChanged += OnVmChanged;
        _ = RenderAsync();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReaderViewModel.Content) or nameof(ReaderViewModel.Zoom) or nameof(ReaderViewModel.Night)) _ = RenderAsync();
    }

    private void OnPageAreaSize(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && _vm?.Content is { Kind: ReaderKind.Pdf }) { _resize.Stop(); _resize.Start(); }
    }

    /// <summary>Draws the current section: text blocks natively, a PDF page with the Windows PDF engine, or a comic page.</summary>
    private async Task RenderAsync()
    {
        if (_vm?.Content is not { } content) return;
        var version = ++_version;
        try
        {
            switch (content.Kind)
            {
                case ReaderKind.Blocks:
                {
                    Busy.Visibility = Visibility.Collapsed; PageScroll.Visibility = Visibility.Collapsed;
                    var doc = FlowDocumentBuilder.Build(content.Blocks, _vm.Zoom, _vm.Night, _vm.Highlight, out var first);
                    Doc.Document = doc; Doc.Visibility = Visibility.Visible;
                    if (first is not null) _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => first.BringIntoView());
                    break;
                }
                case ReaderKind.Pdf:
                {
                    Busy.Visibility = Visibility.Visible;
                    var dip = Math.Max(420, (PageScroll.ActualWidth > 0 ? PageScroll.ActualWidth : 900) - 48) * _vm.Zoom;
                    var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                    var image = await _pdf.RenderAsync(content.FilePath!, content.Page, (int)(dip * dpi), _vm.Night);
                    if (version != _version) return;                       // the user moved on while this page was drawing
                    PageImage.Source = image; PageImage.Width = dip;
                    ShowPage();
                    break;
                }
                case ReaderKind.Image:
                {
                    var image = PhotoImage.Load(content.FilePath, 1800);
                    if (version != _version) return;
                    PageImage.Source = image; PageImage.Width = Math.Max(420, (PageScroll.ActualWidth > 0 ? PageScroll.ActualWidth : 900) - 48) * _vm.Zoom;
                    ShowPage();
                    break;
                }
            }
        }
        catch (PdfProblem ex)
        {
            Busy.Visibility = Visibility.Collapsed;
            if (version == _version) _vm.ErrorMessage = Loc.T(ex.Reason == "locked" ? "reader_pdf_locked" : "reader_pdf_damaged");
        }
    }

    private void ShowPage()
    {
        Busy.Visibility = Visibility.Collapsed; Doc.Visibility = Visibility.Collapsed; PageScroll.Visibility = Visibility.Visible;
        PageScroll.ScrollToTop();
    }

    private void OnHitSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not LibraryHit hit || _vm is null) return;
        ((ListBox)sender).SelectedItem = null;
        _vm.OpenHitCommand.Execute(hit);
    }
}

internal static class LabelPlacement
{
    /// <summary>Centres a label on its place once its size is known.</summary>
    public static void Centre(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement el) return;
        (double X, double Y)? c = el.DataContext switch { PlanShape p => (p.LabelX, p.LabelY), FeatureShape f => (f.LabelX, f.LabelY), _ => null };
        if (c is null) return;
        Canvas.SetLeft(el, c.Value.X - e.NewSize.Width / 2);
        Canvas.SetTop(el, c.Value.Y - e.NewSize.Height / 2);
    }
}
public partial class InteriorView : UserControl
{
    private double _sx, _sy, _dx, _dy, _sw, _sh;
    private Point? _wallStart;
    public InteriorView() => InitializeComponent();
    private ViewModels.InteriorViewModel? Vm => DataContext as ViewModels.InteriorViewModel;
    private static ViewModels.InteriorShape? Shape(object sender) => (sender as FrameworkElement)?.DataContext as ViewModels.InteriorShape;

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Vm is not { } vm || e.OriginalSource is System.Windows.Controls.TextBox) return;
        var ctrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0;
        if (ctrl && e.Key == System.Windows.Input.Key.Z) { vm.UndoCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == System.Windows.Input.Key.Y) { vm.RedoCommand.Execute(null); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Delete && vm.HasSelected) { vm.DeleteCommand.Execute(null); e.Handled = true; }
    }

    private void OnViewportSize(object sender, SizeChangedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var first = vm.ViewportWidth < 50;
        vm.ViewportWidth = e.NewSize.Width; vm.ViewportHeight = e.NewSize.Height;
        if (first) vm.FitView();
    }

    private void OnWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (Vm is not { } vm) return;
        var pos = e.GetPosition(PlanScroll);
        var old = vm.Scale;
        var cx = (PlanScroll.HorizontalOffset + pos.X) / old; var cy = (PlanScroll.VerticalOffset + pos.Y) / old;
        vm.Scale = Math.Clamp(old * (e.Delta > 0 ? 1.15 : 1 / 1.15), 0.08, 4);
        PlanScroll.UpdateLayout();
        PlanScroll.ScrollToHorizontalOffset(cx * vm.Scale - pos.X); PlanScroll.ScrollToVerticalOffset(cy * vm.Scale - pos.Y);
        e.Handled = true;
    }

    private void OnPrintPlan(object sender, RoutedEventArgs e) => Printing.PrintPicture(PlanScroll, Loc.T("nav_interior") + (Vm?.Building is { } b ? " · " + b.Name : ""), Loc.T("print_plan_hint"));

    private void OnShapeDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (Shape(sender) is { } s && Vm is { } vm) vm.Selected = s; }

    private void OnMoveStart(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        if (Shape(sender) is not { } s || Vm is not { } vm) return;
        vm.Selected = s; _sx = s.X; _sy = s.Y; _dx = _dy = 0;
    }
    private void OnMove(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (Shape(sender) is not { } s) return;
        // the thumb is inside the item's rotation: turn the pointer movement back into plan axes
        var r = s.Rotation * Math.PI / 180; var cos = Math.Cos(r); var sin = Math.Sin(r);
        _dx += e.HorizontalChange * cos - e.VerticalChange * sin; _dy += e.HorizontalChange * sin + e.VerticalChange * cos;
        s.X = _sx + _dx; s.Y = _sy + _dy;
    }
    private async void OnMoveDone(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) { if (Shape(sender) is { } s && Vm is { } vm) await vm.MovedAsync(s); }

    private void OnResizeStart(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) { if (Shape(sender) is { } s && Vm is { } vm) { vm.Selected = s; _sw = s.Width; _sh = s.Height; _dx = _dy = 0; } }
    private void OnResize(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (Shape(sender) is not { } s) return;
        _dx += e.HorizontalChange; _dy += e.VerticalChange;
        s.Width = Math.Max(10, ViewModels.InteriorShape.SnapSize(_sw + _dx)); if (!s.IsWall && !s.IsOpening) s.Height = Math.Max(10, ViewModels.InteriorShape.SnapSize(_sh + _dy));
    }
    private async void OnResizeDone(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) { if (Shape(sender) is { } s && Vm is { } vm) await vm.ResizedAsync(s); }

    private void OnDrawDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _wallStart = e.GetPosition(DrawLayer);
        Rubber.X1 = Rubber.X2 = _wallStart.Value.X; Rubber.Y1 = Rubber.Y2 = _wallStart.Value.Y; Rubber.Visibility = Visibility.Visible;
        DrawLayer.CaptureMouse();
    }
    private void OnDrawMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_wallStart is null) return;
        var p = e.GetPosition(DrawLayer); Rubber.X2 = p.X; Rubber.Y2 = p.Y;
    }
    private async void OnDrawUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_wallStart is not { } a) return;
        var b = e.GetPosition(DrawLayer); _wallStart = null; Rubber.Visibility = Visibility.Collapsed; DrawLayer.ReleaseMouseCapture();
        if (Vm is { } vm) await vm.DrawWallAsync(a.X, a.Y, b.X, b.Y);
    }
}
