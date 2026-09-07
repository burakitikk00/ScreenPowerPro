using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenPowerPro.Models;
using ScreenPowerPro.Services;

namespace ScreenPowerPro.ViewModels;

/// <summary>
/// Kayıt sırasında ekranda kalan küçük kontrol çubuğunun (RecordingBar) ViewModel'ı.
/// Süre gösterimi, duraklatma/devam etme ve kaydı sonlandırıp projeyi oluşturma mantığını yönetir.
/// </summary>
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

    [ObservableProperty]
    private bool _isPaused;

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

    private int _recordingWidth = 1920;
    private int _recordingHeight = 1080;
    private int _originX = 0;
    private int _originY = 0;

    public void SetActiveProject(string projectDir, int width = 1920, int height = 1080, int originX = 0, int originY = 0)
    {
        _activeProjectDir = projectDir;
        _recordingWidth = width > 0 ? width : 1920;
        _recordingHeight = height > 0 ? height : 1080;
        _originX = originX;
        _originY = originY;

        IsRecording = true;
        IsPaused = false;
        ElapsedTime = "00:00:00";
    }

    /// <summary>
    /// Kaydı duraklatır veya duraklatılmış kaydı devam ettirir.
    /// </summary>
    [RelayCommand]
    public void TogglePause()
    {
        if (!IsRecording) return;
        IsPaused = !IsPaused;
        // İleride FFmpeg pause/resume sinyali genişletilebilir
    }

    /// <summary>
    /// Kaydı sonlandırır, fare/klavye olaylarını kaydeder, otomatik zoom efektlerini
    /// hesaplar ve göreceli dosya yollarıyla proje manifestosunu kaydeder.
    /// </summary>
    [RelayCommand]
    public async Task StopRecordingAsync()
    {
        if (!IsRecording || string.IsNullOrEmpty(_activeProjectDir)) return;

        IsRecording = false;

        // 1. Video kaydını ve FFmpeg sürecini durdur
        await _recorderService.StopRecordingAsync();

        // 2. Win32 Low-Level Hook giriş takibini durdur
        _inputTracker.StopTracking();

        // 3. Ham giriş verilerini proje klasörüne JSON olarak kaydet
        _projectService.SaveMouseClicks(_activeProjectDir, _inputTracker.Clicks);
        _projectService.SaveMouseMoves(_activeProjectDir, _inputTracker.Moves);
        _projectService.SaveKeystrokes(_activeProjectDir, _inputTracker.Keystrokes);

        double duration = _recorderService.ElapsedSeconds;

        string recFolder = Path.Combine(_activeProjectDir, "recording");
        string micFullPath = Path.Combine(recFolder, "microphone-0.wav");
        string sysFullPath = Path.Combine(recFolder, "system_audio-0.wav");
        bool hasMicFile = File.Exists(micFullPath) && new FileInfo(micFullPath).Length > 200;
        bool hasSysFile = File.Exists(sysFullPath) && new FileInfo(sysFullPath).Length > 200;

        // 4. Proje manifestosunu göreceli yollarla oluştur (Taşınabilirlik için kritik)
        var manifest = new ProjectManifest
        {
            ProjectName = Path.GetFileName(_activeProjectDir),
            VideoPath = "./recording/display-0.mp4",
            MicAudioPath = hasMicFile ? "./recording/microphone-0.wav" : null,
            SystemAudioPath = hasSysFile ? "./recording/system_audio-0.wav" : null,
            Metadata = new RecordingMetadata
            {
                Width = _recordingWidth,
                Height = _recordingHeight,
                OriginX = _originX,
                OriginY = _originY,
                DurationSeconds = duration,
                Fps = _settingsService.Current.Fps,
                HasMicAudio = hasMicFile,
                HasSystemAudio = hasSysFile
            }
        };

        // 5. Otomatik zoom efektlerini ZoomEngineService ve kümeleme ile hesapla
        if (_settingsService.Current.AutoZoom)
        {
            manifest.Timeline.ZoomEffects = _inputTracker.GenerateAutoZoomEffects(
                maxVideoDurationSec: duration,
                autoZoomMode: _settingsService.Current.AutoZoomMode);
        }

        // 6. İlk tam boy klip segmentlerini oluştur
        if (duration > 0)
        {
            manifest.Timeline.VideoTrack.Clips.Add(new ClipSegment
            {
                Id = $"clip-video-{Guid.NewGuid():N}",
                SourceStart = 0,
                SourceEnd = duration,
                TrackOffset = 0
            });

            if (hasMicFile)
            {
                manifest.Timeline.MicTrack.Clips.Add(new ClipSegment
                {
                    Id = $"clip-mic-{Guid.NewGuid():N}",
                    SourceStart = 0,
                    SourceEnd = duration,
                    TrackOffset = 0
                });
            }

            if (hasSysFile)
            {
                manifest.Timeline.SysTrack.Clips.Add(new ClipSegment
                {
                    Id = $"clip-sys-{Guid.NewGuid():N}",
                    SourceStart = 0,
                    SourceEnd = duration,
                    TrackOffset = 0
                });
            }
        }

        _projectService.SaveProject(_activeProjectDir, manifest);

        RecordingFinished?.Invoke(_activeProjectDir);
    }
}
