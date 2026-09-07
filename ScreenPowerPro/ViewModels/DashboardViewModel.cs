using System;
using System.Collections.ObjectModel;
using System.Linq;
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
    private readonly LocalizationService _loc;
    private readonly ProjectService _projectService;
    private readonly ScreenRecorderService _recorderService;
    private readonly InputTrackerService _inputTracker;

    public DeviceManagerService DeviceManager { get; }

    [ObservableProperty]
    private RecordingMode _selectedMode = RecordingMode.FullScreen;

    [ObservableProperty]
    private string _activeModeName = "FullScreen";

    [ObservableProperty]
    private string _cameraDisplayName = "None";

    [ObservableProperty]
    private string _micDisplayName = "None";

    [ObservableProperty]
    private string _speakerDisplayName = "None";

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

    public int LastCropX { get; set; }
    public int LastCropY { get; set; }
    public int LastCropWidth { get; set; }
    public int LastCropHeight { get; set; }

    public event Action<string>? RequestStartRecording; // passes projectDir

    public DashboardViewModel(
        SettingsService settingsService,
        LocalizationService localizationService,
        ProjectService projectService,
        ScreenRecorderService recorderService,
        InputTrackerService inputTracker,
        DeviceManagerService deviceManager)
    {
        _settingsService = settingsService;
        _loc = localizationService;
        _projectService = projectService;
        _recorderService = recorderService;
        _inputTracker = inputTracker;
        DeviceManager = deviceManager;

        _loc.LanguageChanged += UpdateDeviceDisplayNames;
        DeviceManager.DevicesUpdated += UpdateDeviceDisplayNames;
        DeviceManager.AudioAppsUpdated += UpdateDeviceDisplayNames;

        LoadSettings();
        RefreshWindows();
        RefreshRecentProjects();
    }

    public async Task InitializeDevicesAsync()
    {
        await DeviceManager.RefreshAllDevicesAsync();
        UpdateDeviceDisplayNames();
    }

    public void UpdateDeviceDisplayNames()
    {
        // 1. Camera Name
        if (DeviceManager.SelectedCamera != null && !DeviceManager.SelectedCamera.IsNone)
        {
            string name = DeviceManager.SelectedCamera.Name;
            CameraDisplayName = name.Length > 16 ? name.Substring(0, 14) + "..." : name;
            IsCameraEnabled = true;
        }
        else
        {
            CameraDisplayName = _loc["None"];
            IsCameraEnabled = false;
        }

        // 2. Microphone Name
        if (DeviceManager.SelectedMicrophone != null && !DeviceManager.SelectedMicrophone.IsNone)
        {
            string name = DeviceManager.SelectedMicrophone.Name;
            MicDisplayName = name.Length > 16 ? name.Substring(0, 14) + "..." : name;
            IsMicEnabled = true;
        }
        else
        {
            MicDisplayName = _loc["None"];
            IsMicEnabled = false;
        }

        // 3. Speaker / App Audio Name
        if (DeviceManager.IsOnlyAppAudioSelected)
        {
            int count = DeviceManager.GetSelectedAppCount();
            SpeakerDisplayName = _loc.Get("Dashboard_OnlyAppCount", count);
            IsSystemAudioEnabled = true;
        }
        else if (DeviceManager.SelectedSpeaker != null && !DeviceManager.SelectedSpeaker.IsNone)
        {
            string name = DeviceManager.SelectedSpeaker.Name;
            SpeakerDisplayName = name.Length > 16 ? name.Substring(0, 14) + "..." : name;
            IsSystemAudioEnabled = true;
        }
        else
        {
            SpeakerDisplayName = _loc["None"];
            IsSystemAudioEnabled = false;
        }
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

    public string ImportVideo(string sourcePath)
    {
        string projectDir = _projectService.ImportVideoProject(sourcePath);
        RefreshRecentProjects();
        return projectDir;
    }

    public bool DeleteProject(string projectDir)
    {
        bool success = _projectService.DeleteProject(projectDir);
        if (success)
        {
            RefreshRecentProjects();
        }
        return success;
    }

    public bool RenameProject(string projectDir, string newName)
    {
        bool success = _projectService.RenameProject(projectDir, newName);
        if (success)
        {
            RefreshRecentProjects();
        }
        return success;
    }

    [RelayCommand]
    public async Task StartRecordingAsync()
    {
        SaveSettings();

        int cropX = 0, cropY = 0, cropW = 1920, cropH = 1080;

        if (SelectedMode == RecordingMode.Region)
        {
            if (LastCropWidth > 0 && LastCropHeight > 0)
            {
                cropX = LastCropX;
                cropY = LastCropY;
                cropW = LastCropWidth;
                cropH = LastCropHeight;
            }
            else
            {
                var regionWindow = new Views.RegionSelectionWindow();
                regionWindow.Activate();

                var result = await regionWindow.WaitForSelectionAsync();
                if (result == null)
                {
                    // User cancelled region selection
                    return;
                }

                cropX = (int)result.Value.X;
                cropY = (int)result.Value.Y;
                cropW = (int)result.Value.Width;
                cropH = (int)result.Value.Height;
                
                // Ensure even numbers for video dimensions (FFmpeg x264 requirement)
                if (cropW % 2 != 0) cropW++;
                if (cropH % 2 != 0) cropH++;

                LastCropX = cropX;
                LastCropY = cropY;
                LastCropWidth = cropW;
                LastCropHeight = cropH;
            }
        }

        // 1. Create project dir
        string projectDir = _projectService.CreateNewProjectDirectory();

        // 2. Start input tracker
        _inputTracker.StartTracking();

        // 3. Start recorder
        IntPtr? winHandle = SelectedMode == RecordingMode.Window && SelectedWindow != null ? SelectedWindow.Handle : null;
        await _recorderService.StartRecordingAsync(projectDir, SelectedMode, winHandle, cropX, cropY, cropW, cropH);

        RequestStartRecording?.Invoke(projectDir);
    }
}
