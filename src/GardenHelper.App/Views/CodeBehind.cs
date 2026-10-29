using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using GardenHelper.App.Infrastructure;
using GardenHelper.App.ViewModels;
using GardenHelper.Core.Data;
using GardenHelper.Core.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Wpf;

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

public partial class SiteView : UserControl { public SiteView() => InitializeComponent(); }
public partial class MonitorView : UserControl { public MonitorView() => InitializeComponent(); }

public partial class DesignerView : UserControl
{
    // Raw (unsnapped) position while dragging, so snapping never swallows small movements.
    private double _rawA, _rawB;

    public DesignerView() => InitializeComponent();

    private static BedItem? Bed(object sender) => (sender as FrameworkElement)?.DataContext as BedItem;
    private DesignerViewModel? Vm => DataContext as DesignerViewModel;

    private void OnBedDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Bed(sender) is { } b && Vm is { } vm) vm.Selected = b;
    }

    private void OnMoveStart(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        if (Bed(sender) is { } b) { _rawA = b.X; _rawB = b.Y; if (Vm is { } vm) vm.Selected = b; }
    }

    private void OnMove(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (Bed(sender) is not { } b || Vm is not { } vm) return;
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
        if (Bed(sender) is not { } b || Vm is not { } vm) return;
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

    // ---- Canvas navigation ----
    private bool _panning; private Point _panStart; private double _panH, _panV;

    private void OnWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control || Vm is not { } vm) return;
        vm.Scale = Math.Clamp(Math.Round(vm.Scale * (e.Delta > 0 ? 1.15 : 1 / 1.15), 3), 0.03, 3);
        e.Handled = true;
    }

    private void OnPanStart(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Middle) return;
        _panning = true; _panStart = e.GetPosition(CanvasScroll); _panH = CanvasScroll.HorizontalOffset; _panV = CanvasScroll.VerticalOffset;
        CanvasScroll.CaptureMouse(); CanvasScroll.Cursor = System.Windows.Input.Cursors.Hand; e.Handled = true;
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
        if (!_panning || e.ChangedButton != System.Windows.Input.MouseButton.Middle) return;
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
    private bool _webReady;

    public ReaderView()
    {
        InitializeComponent();
        Web.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = App.Services.GetRequiredService<AppPaths>().WebViewDataDir,
        };
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => { if (_vm is not null) _vm.PropertyChanged -= OnVmChanged; Web.Dispose(); };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as ReaderViewModel;
        if (_vm is null) return;
        _vm.PropertyChanged += OnVmChanged;
        _ = ShowTargetAsync();
    }

    private async void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ReaderViewModel.Target): await ShowTargetAsync(); break;
            case nameof(ReaderViewModel.Zoom): if (_webReady) Web.ZoomFactor = _vm!.Zoom; break;
            case nameof(ReaderViewModel.Night): await ApplyNightAsync(); break;
        }
    }

    private async Task<bool> EnsureWebAsync()
    {
        if (_webReady) return true;
        try
        {
            await Web.EnsureCoreWebView2Async();
            Web.NavigationCompleted += OnNavigationCompleted;
            _webReady = true;
            return true;
        }
        catch (Exception ex)
        {
            // Any failure to start the embedded browser (runtime missing, profile folder locked...) falls back to the text reader.
            Serilog.Log.Warning(ex, "WebView2 is not available; using the plain-text reader");
            if (_vm is not null) _vm.WebViewUnavailable = true;
            return false;
        }
    }

    private async Task ShowTargetAsync()
    {
        if (_vm?.Target is not { } target) return;
        if (!await EnsureWebAsync()) return;
        Web.CoreWebView2.Navigate(target.Uri.AbsoluteUri);
    }

    private async void OnNavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_vm is null) return;
        Web.ZoomFactor = _vm.Zoom;
        await ApplyNightAsync();
        if (_vm.Highlight is { Length: > 0 } term && _vm.Target is { CanHighlight: true })
            await Web.ExecuteScriptAsync(HighlightScript.Replace("%TERM%", JsonSerializer.Serialize(term)));
    }

    private async Task ApplyNightAsync()
    {
        if (!_webReady || _vm is null) return;
        var on = _vm.Night && _vm.Target is { IsPdf: false };
        await Web.ExecuteScriptAsync(on
            ? "document.documentElement.style.filter='invert(0.9) hue-rotate(180deg)';document.documentElement.style.background='#fff';" +
              "document.querySelectorAll('img,video').forEach(e=>e.style.filter='invert(1) hue-rotate(180deg)');"
            : "document.documentElement.style.filter='';document.querySelectorAll('img,video').forEach(e=>e.style.filter='');");
    }

    private void OnHitSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not LibraryHit hit || _vm is null) return;
        ((ListBox)sender).SelectedItem = null;
        _vm.OpenHitCommand.Execute(hit);
    }

    /// <summary>Highlights the search words in the page, ignoring diacritics, and scrolls to the first match.</summary>
    private const string HighlightScript = """
        (function(term){
          var map={a:'aăâáàäå',e:'eéèêë',i:'iîíìï',o:'oóòôöő',u:'uúùûüű',s:'sșşś',t:'tțţ',c:'cçć',n:'nñń',z:'zźż'};
          var words=term.split(/[^\p{L}\p{N}]+/u).filter(Boolean).map(function(w){
            return Array.from(w.toLowerCase()).map(function(ch){
              var base=ch.normalize('NFD').replace(/[̀-ͯ]/g,'');
              var cls=map[base]; if(cls) return '['+cls+']';
              return ch.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');
            }).join('');
          });
          if(!words.length||!document.body) return;
          var re=new RegExp('('+words.join('|')+')','gi'), test=new RegExp(re.source,'i');
          var walker=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT,null), nodes=[];
          while(walker.nextNode()){var n=walker.currentNode,p=n.parentNode;
            if(p&&!/^(SCRIPT|STYLE|MARK|NOSCRIPT)$/.test(p.nodeName)&&test.test(n.nodeValue)) nodes.push(n);}
          nodes.forEach(function(n){var span=document.createElement('span');
            span.innerHTML=n.nodeValue.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(re,'<mark style="background:#f3c969;color:#000">$1</mark>');
            n.parentNode.replaceChild(span,n);});
          var m=document.querySelector('mark'); if(m) m.scrollIntoView({block:'center'});
        })(%TERM%);
        """;
}
