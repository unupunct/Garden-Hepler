using System.IO;
using System.Windows;
using System.Windows.Threading;
using GardenHelper.App.Infrastructure;
using GardenHelper.App.ViewModels;
using GardenHelper.Core.Data;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Library;
using GardenHelper.Core.Services;
using GardenHelper.Core.Site;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace GardenHelper.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private ILogger<App>? _log;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = new AppPaths();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(paths.LogDir, "gardenhelper-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log.Fatal(ev.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, ev) => { Log.Error(ev.Exception, "Unobserved task exception"); ev.SetObserved(); };

        try
        {
            await DbFactory.InitializeAsync(paths);
            var kb = KnowledgeBase.Load(new[] { Path.Combine(AppContext.BaseDirectory, "Content"), paths.UserContentDir });

            var sc = new ServiceCollection();
            sc.AddLogging(b => b.AddSerilog(dispose: true));
            sc.AddSingleton(paths);
            sc.AddSingleton<IDbContextFactory<GardenDbContext>>(new ContextFactory(paths));
            sc.AddSingleton(kb);
            sc.AddSingleton<Repository>();
            sc.AddSingleton<SettingsService>();
            sc.AddSingleton<BackupService>();
            sc.AddSingleton<LibraryService>();
            sc.AddSingleton<ReaderPreparer>();
            sc.AddSingleton<GlobalSearchService>();
            sc.AddSingleton<Preferences>();
            sc.AddSingleton<AlertService>();
            sc.AddSingleton(_ =>
            {
                var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(40) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("GardenHelper/0.1 (+https://github.com/unupunct/Garden-Hepler)");
                return http;
            });
            sc.AddSingleton<ClimateService>();
            sc.AddSingleton<GardenHelper.Core.Cameras.CameraClient>();
            sc.AddSingleton<CameraFeed>();
            sc.AddSingleton<SatelliteTileService>();
            sc.AddSingleton<BackdropService>();
            sc.AddSingleton<IDialogs, WpfDialogs>();
            sc.AddSingleton<ShellViewModel>();
            sc.AddSingleton<INavigator>(p => p.GetRequiredService<ShellViewModel>());
            foreach (var vm in new[] { typeof(DashboardViewModel), typeof(TasksViewModel), typeof(PlantsViewModel), typeof(SeedsViewModel), typeof(JournalViewModel),
                                       typeof(LibraryViewModel), typeof(ReaderViewModel), typeof(SearchViewModel), typeof(GardenViewModel), typeof(SettingsViewModel), typeof(AboutViewModel),
                                       typeof(SiteViewModel), typeof(MapViewModel), typeof(PropertyViewModel), typeof(SpaceViewModel), typeof(DesignerViewModel), typeof(MonitorViewModel), typeof(CamerasViewModel), typeof(AnimalsViewModel), typeof(ComingUpViewModel), typeof(BudgetViewModel) })
                sc.AddTransient(vm);
            Services = sc.BuildServiceProvider();
            _log = Services.GetRequiredService<ILogger<App>>();
            _log.LogInformation("Garden Helper starting. Data folder: {Root}. Plants: {Plants}", paths.Root, kb.Plants.Count);
            foreach (var problem in kb.LoadErrors) _log.LogWarning("Content problem: {Problem}", problem);

            await Services.GetRequiredService<Preferences>().LoadAsync();
            var window = new MainWindow(Services.GetRequiredService<ShellViewModel>());
            MainWindow = window;
            window.Show();
            Services.GetRequiredService<ShellViewModel>().Start();

            _ = Task.Run(async () =>
            {
                try { await Services.GetRequiredService<BackupService>().AutoBackupIfDueAsync(TimeSpan.FromDays(7)); }
                catch (Exception ex) { _log.LogWarning(ex, "Automatic backup failed"); }
            });
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed");
            MessageBox.Show($"Garden Helper could not start.\n\n{ex.Message}\n\nDetails were written to:\n{paths.LogDir}", "Garden Helper", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        MessageBox.Show(Loc.F("err_unexpected", e.Exception.Message), "Garden Helper", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
