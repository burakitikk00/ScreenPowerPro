using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenPowerPro.Models;
using ScreenPowerPro.Services;

namespace ScreenPowerPro.ViewModels;

public partial class RecordingBarViewModel : ObservableObject
{
    private readonly ScreenRecorderService _recorderService;
    private readonly InputTrackerService _inputTracker;
    private readonly ProjectService _projectService;
    private readonly SettingsService _settingsService;

    [ObservableProperty]
    private string _elapsedTime = "00:00:00";

    [ObservableProperty]
    private bool _isRecording;

    private string? _activeProjectDir;

    public event Action<string>? RecordingFinished; // passes projectDir

    public RecordingBarViewModel(
        ScreenRecorderService recorderService,
        InputTrackerService inputTracker,
        ProjectService projectService,
        SettingsService settingsService)
    {
        _recorderService = recorderService;
        _inputTracker = inputTracker;
        _projectService = projectService;
        _settingsService = settingsService;

        _recorderService.DurationUpdated += (seconds) =>
        {
            var ts = TimeSpan.FromSeconds(seconds);
            ElapsedTime = ts.ToString(@"hh\:mm\:ss");
        };
    }

    public void SetActiveProject(string projectDir)
    {
        _activeProjectDir = projectDir;
        IsRecording = true;
        ElapsedTime = "00:00:00";
    }

    [RelayCommand]
    public async Task StopRecordingAsync()
    {
        if (!IsRecording || string.IsNullOrEmpty(_activeProjectDir)) return;

        IsRecording = false;

        // 1. Stop video recording
        await _recorderService.StopRecordingAsync();

        // 2. Stop input tracking
        _inputTracker.StopTracking();

        // 3. Save raw input logs
        _projectService.SaveMouseClicks(_activeProjectDir, _inputTracker.Clicks);
        _projectService.SaveMouseMoves(_activeProjectDir, _inputTracker.Moves);
        _projectService.SaveKeystrokes(_activeProjectDir, _inputTracker.Keystrokes);

        // 4. Generate Auto Zoom Effects if enabled
        var manifest = new ProjectManifest
        {
            ProjectName = Path.GetFileName(_activeProjectDir),
            VideoPath = Path.Combine(_activeProjectDir, "recording", "display-0.mp4"),
            MicAudioPath = Path.Combine(_activeProjectDir, "recording", "microphone-0.wav"),
            SystemAudioPath = Path.Combine(_activeProjectDir, "recording", "system_audio-0.wav"),
            Metadata = new RecordingMetadata
            {
                DurationSeconds = _recorderService.ElapsedSeconds,
                Fps = _settingsService.Current.Fps,
                HasMicAudio = _settingsService.Current.MicAudioEnabled,
                HasSystemAudio = _settingsService.Current.SystemAudioEnabled
            }
        };

        if (_settingsService.Current.AutoZoom)
        {
            manifest.Timeline.ZoomEffects = _inputTracker.GenerateAutoZoomEffects(_recorderService.ElapsedSeconds);
        }

        _projectService.SaveProject(_activeProjectDir, manifest);

        RecordingFinished?.Invoke(_activeProjectDir);
    }
}
