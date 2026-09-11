using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using ScreenPowerPro.Services;
using ScreenPowerPro.ViewModels;

namespace ScreenPowerPro;

public partial class App : Application
{
    private Window? _window;

    public IServiceProvider Services { get; }
    public static new App Current => (App)Application.Current;

    public static bool ShowIntegratedGpuWarning { get; set; }

    public App()
    {
        ScreenPowerPro.Helpers.AppLog.InitializeConsole();
        ScreenPowerPro.Helpers.AppLog.Info("Uygulama başlatılıyor (App ctor)...");
        var gpuService = new GpuOptimizationService();
        gpuService.Initialize();
        ShowIntegratedGpuWarning = gpuService.IsRunningOnIntegratedGpu;

        UnhandledException += (s, e) =>
        {
            ScreenPowerPro.Helpers.AppLog.Error($"[App.UnhandledException] {e.Message}", e.Exception);
            try { System.IO.File.AppendAllText(@"C:\Users\burak\ScreenPowerPro\global_crash.log", $"[App.UnhandledException] {e.Message}\n{e.Exception}\n"); } catch { }
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                ScreenPowerPro.Helpers.AppLog.Error("[AppDomain.UnhandledException] Yakalanmamış sistem hatası", ex);
            }
            else
            {
                ScreenPowerPro.Helpers.AppLog.Error($"[AppDomain.UnhandledException] {e.ExceptionObject}");
            }
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            ScreenPowerPro.Helpers.AppLog.Error("[TaskScheduler.UnobservedTaskException]", e.Exception);
            e.SetObserved();
        };

        InitializeComponent();

        var services = new ServiceCollection();

        // Register Services
        services.AddSingleton<SettingsService>();
        services.AddSingleton(SettingsManager.Instance);
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<DeviceManagerService>();
        services.AddSingleton<ProjectService>();
        services.AddSingleton<ZoomEngineService>();
        services.AddSingleton<InputTrackerService>();
        services.AddSingleton<ScreenRecorderService>();
        services.AddSingleton<CursorRenderService>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<AudioLevelMonitorService>();
        services.AddSingleton<AudioWaveformService>();
        services.AddSingleton<TimelineMathService>();
        services.AddSingleton<GpuOptimizationService>();

        // Register ViewModels
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<RecordingBarViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<EditorViewModel>();
        services.AddSingleton<ExportViewModel>();
        services.AddSingleton<SettingsViewModel>();

        Services = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
