using System.Windows;
using System.Windows.Input;
using GardenHelper.App.ViewModels;

namespace GardenHelper.App;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel shell)
    {
        InitializeComponent();
        DataContext = shell;
    }

    /// <summary>Ctrl+K or Ctrl+F focuses the global search box; Alt+1..5 jump to the main pages.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.K || e.Key == Key.F))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && DataContext is ShellViewModel shell)
        {
            var page = e.SystemKey switch
            {
                Key.D1 => typeof(DashboardViewModel), Key.D2 => typeof(TasksViewModel), Key.D3 => typeof(PlantsViewModel),
                Key.D4 => typeof(SeedsViewModel), Key.D5 => typeof(LibraryViewModel), _ => null,
            };
            if (page is not null)
            {
                shell.SelectedNav = shell.Nav.FirstOrDefault(n => n.Page == page);
                e.Handled = true;
            }
        }
    }
}
