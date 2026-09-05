using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenPowerPro.Models;
using ScreenPowerPro.Services;

namespace ScreenPowerPro.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    [ObservableProperty]
    private string _projectSaveLocation = string.Empty;

    [ObservableProperty]
    private string _exportLocation = string.Empty;

    [ObservableProperty]
    private bool _autoZoom;

    [ObservableProperty]
    private bool _hideDesktopIcons;

    [ObservableProperty]
    private bool _hideTaskbar;

    [ObservableProperty]
    private bool _hideMouseCursor;

    [ObservableProperty]
    private bool _excludeAppFromRecording;

    [ObservableProperty]
    private string _resolution = "1080p";

    [ObservableProperty]
    private int _countdownSeconds = 3;

    [ObservableProperty]
    private int _fps = 60;

    [ObservableProperty]
    private string _exportFormat = "mp4";

    [ObservableProperty]
    private bool _micAudioEnabled = true;

    [ObservableProperty]
    private bool _systemAudioEnabled = true;

    [ObservableProperty]
    private string _saveStatusMessage = string.Empty;

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        Load();
    }

    public void Load()
    {
        var s = _settingsService.Current;
        ProjectSaveLocation = s.ProjectSaveLocation;
        ExportLocation = s.ExportLocation;
        AutoZoom = s.AutoZoom;
        HideDesktopIcons = s.HideDesktopIcons;
        HideTaskbar = s.HideTaskbar;
        HideMouseCursor = s.HideMouseCursor;
        ExcludeAppFromRecording = s.ExcludeAppFromRecording;
        Resolution = s.Resolution;
        CountdownSeconds = s.CountdownSeconds;
        Fps = s.Fps;
        ExportFormat = s.ExportFormat;
        MicAudioEnabled = s.MicAudioEnabled;
        SystemAudioEnabled = s.SystemAudioEnabled;
    }

    [RelayCommand]
    public void Save()
    {
        var s = _settingsService.Current;
        s.ProjectSaveLocation = ProjectSaveLocation;
        s.ExportLocation = ExportLocation;
        s.AutoZoom = AutoZoom;
        s.HideDesktopIcons = HideDesktopIcons;
        s.HideTaskbar = HideTaskbar;
        s.HideMouseCursor = HideMouseCursor;
        s.ExcludeAppFromRecording = ExcludeAppFromRecording;
        s.Resolution = Resolution;
        s.CountdownSeconds = CountdownSeconds;
        s.Fps = Fps;
        s.ExportFormat = ExportFormat;
        s.MicAudioEnabled = MicAudioEnabled;
        s.SystemAudioEnabled = SystemAudioEnabled;

        _settingsService.Save();
        SaveStatusMessage = "Ayarlar başarıyla kaydedildi.";
    }
}
