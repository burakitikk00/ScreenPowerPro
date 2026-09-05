using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenPowerPro.Helpers;
using ScreenPowerPro.Models;
using ScreenPowerPro.Services;

namespace ScreenPowerPro.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly ProjectService _projectService;
    private readonly ScreenRecorderService _recorderService;
    private readonly InputTrackerService _inputTracker;

    [ObservableProperty]
    private RecordingMode _selectedMode = RecordingMode.FullScreen;

    [ObservableProperty]
    private bool _isMicEnabled;

    [ObservableProperty]
    private bool _isSystemAudioEnabled;

    [ObservableProperty]
    private bool _isCameraEnabled;

    [ObservableProperty]
    private bool _isAutoZoomEnabled;

    [ObservableProperty]
    private bool _isHideCursorEnabled;

    [ObservableProperty]
    private string _selectedResolution = "1080p";

    [ObservableProperty]
    private int _countdownSeconds = 3;

    [ObservableProperty]
    private Win32Helper.WindowInfo? _selectedWindow;

    [ObservableProperty]
    private ObservableCollection<Win32Helper.WindowInfo> _windows = new();

    [ObservableProperty]
    private ObservableCollection<ProjectInfo> _recentProjects = new();

    public event Action<string>? RequestStartRecording; // passes projectDir

    public DashboardViewModel(
        SettingsService settingsService,
        ProjectService projectService,
        ScreenRecorderService recorderService,
        InputTrackerService inputTracker)
    {
        _settingsService = settingsService;
        _projectService = projectService;
        _recorderService = recorderService;
        _inputTracker = inputTracker;

        LoadSettings();
        RefreshWindows();
        RefreshRecentProjects();
    }

    private void LoadSettings()
    {
        var s = _settingsService.Current;
        IsMicEnabled = s.MicAudioEnabled;
        IsSystemAudioEnabled = s.SystemAudioEnabled;
        IsCameraEnabled = s.CameraEnabled;
        IsAutoZoomEnabled = s.AutoZoom;
        IsHideCursorEnabled = s.HideMouseCursor;
        SelectedResolution = s.Resolution;
        CountdownSeconds = s.CountdownSeconds;
    }

    [RelayCommand]
    public void RefreshWindows()
    {
        Windows.Clear();
        foreach (var w in Win32Helper.GetCapturableWindows())
        {
            Windows.Add(w);
        }
        if (Windows.Count > 0)
        {
            SelectedWindow = Windows[0];
        }
    }

    [RelayCommand]
    public void RefreshRecentProjects()
    {
        RecentProjects.Clear();
        foreach (var p in _projectService.GetRecentProjects())
        {
            RecentProjects.Add(p);
        }
    }

    [RelayCommand]
    public void SaveSettings()
    {
        var s = _settingsService.Current;
        s.MicAudioEnabled = IsMicEnabled;
        s.SystemAudioEnabled = IsSystemAudioEnabled;
        s.CameraEnabled = IsCameraEnabled;
        s.AutoZoom = IsAutoZoomEnabled;
        s.HideMouseCursor = IsHideCursorEnabled;
        s.Resolution = SelectedResolution;
        s.CountdownSeconds = CountdownSeconds;
        _settingsService.Save();
    }

    [RelayCommand]
    public async Task StartRecordingAsync()
    {
        SaveSettings();

        // 1. Create project dir
        string projectDir = _projectService.CreateNewProjectDirectory();

        // 2. Start input tracker
        _inputTracker.StartTracking();

        // 3. Start recorder
        IntPtr? winHandle = SelectedMode == RecordingMode.Window && SelectedWindow != null ? SelectedWindow.Handle : null;
        await _recorderService.StartRecordingAsync(projectDir, SelectedMode, winHandle);

        RequestStartRecording?.Invoke(projectDir);
    }
}
