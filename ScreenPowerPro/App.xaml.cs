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

    public App()
    {
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
