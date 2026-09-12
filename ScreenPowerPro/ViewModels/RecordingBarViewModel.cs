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
    /// Tüm ağır I/O ve hesaplama işlemleri Task.Run ile arka plan thread'ine offload edilir
    /// böylece UI thread (ve ProgressRing animasyonu) bloklanmaz.
    /// </summary>
    [RelayCommand]
    public async Task StopRecordingAsync()
    {
        if (!IsRecording || string.IsNullOrEmpty(_activeProjectDir)) return;

        IsRecording = false;

        // Capture snapshot of all state needed on the background thread
        // (avoids cross-thread access to service internals after tracking stops)
        string projectDir = _activeProjectDir;

        // ── STEP 1: Stop the Win32 low-level input hook (fast, signal-only) ──
        _inputTracker.StopTracking();

        // Snapshot all input data BEFORE entering Task.Run to avoid concurrent access
        var clicks     = _inputTracker.Clicks;
        var moves      = _inputTracker.Moves;
        var keystrokes = _inputTracker.Keystrokes;
        bool autoZoom   = _settingsService.Current.AutoZoom;
        string autoZoomMode = _settingsService.Current.AutoZoomMode;
        double maxZoomRatio = _settingsService.Current.MaxZoomRatio;
        int fps             = _settingsService.Current.Fps;
        int recWidth        = _recordingWidth  > 0 ? _recordingWidth  : 1920;
        int recHeight       = _recordingHeight > 0 ? _recordingHeight : 1080;
        int origX           = _originX;
        int origY           = _originY;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // ── STEP 2: Offload ALL disk I/O, FFmpeg finalization + heavy computation to a ThreadPool thread ──
        //    This keeps the UI thread free so the ProgressRing keeps spinning continuously.
        await Task.Run(async () =>
        {
            // 2a. Stop FFmpeg and audio capture (completely backgrounded)
            await _recorderService.StopRecordingAsync();

            double duration = _recorderService.ElapsedSeconds;

            // 3a. Save raw input event logs as JSON
            _projectService.SaveMouseClicks(projectDir, clicks);
            _projectService.SaveMouseMoves(projectDir, moves);
            _projectService.SaveKeystrokes(projectDir, keystrokes);

            string recFolder  = Path.Combine(projectDir, "recording");
            string micFullPath = Path.Combine(recFolder, "microphone-0.wav");
            string sysFullPath = Path.Combine(recFolder, "system_audio-0.wav");
            bool hasMicFile = File.Exists(micFullPath) && new FileInfo(micFullPath).Length > 200;
            bool hasSysFile = File.Exists(sysFullPath) && new FileInfo(sysFullPath).Length > 200;

            // 3b. Build project manifest with relative paths (portable)
            var manifest = new ProjectManifest
            {
                ProjectName    = Path.GetFileName(projectDir),
                VideoPath      = "./recording/display-0.mp4",
                MicAudioPath   = hasMicFile ? "./recording/microphone-0.wav" : null,
                SystemAudioPath = hasSysFile ? "./recording/system_audio-0.wav" : null,
                Metadata = new RecordingMetadata
                {
                    Width           = recWidth,
                    Height          = recHeight,
                    OriginX         = origX,
                    OriginY         = origY,
                    DurationSeconds = duration,
                    Fps             = fps,
                    HasMicAudio     = hasMicFile,
                    HasSystemAudio  = hasSysFile
                }
            };

            // 3c. Generate auto-zoom effects (CPU-intensive clustering)
            if (autoZoom)
            {
                manifest.Timeline.ZoomEffects = _inputTracker.GenerateAutoZoomEffects(
                    maxVideoDurationSec: duration,
                    autoZoomMode:        autoZoomMode,
                    defaultScale:        maxZoomRatio > 1.0 ? maxZoomRatio : 1.5,
                    videoWidth:          recWidth,
                    videoHeight:         recHeight);
            }

            // 3d. Build initial full-length clip segments for every track
            if (duration > 0)
            {
                manifest.Timeline.VideoTrack.Clips.Add(new ClipSegment
                {
                    Id          = $"clip-video-{Guid.NewGuid():N}",
                    SourceStart = 0,
                    SourceEnd   = duration,
                    TrackOffset = 0
                });

                if (hasMicFile)
                {
                    manifest.Timeline.MicTrack.Clips.Add(new ClipSegment
                    {
                        Id          = $"clip-mic-{Guid.NewGuid():N}",
                        SourceStart = 0,
                        SourceEnd   = duration,
                        TrackOffset = 0
                    });
                }

                if (hasSysFile)
                {
                    manifest.Timeline.SysTrack.Clips.Add(new ClipSegment
                    {
                        Id          = $"clip-sys-{Guid.NewGuid():N}",
                        SourceStart = 0,
                        SourceEnd   = duration,
                        TrackOffset = 0
                    });
                }
            }

            // 3e. Flush manifest JSON to disk
            _projectService.SaveProject(projectDir, manifest);
        });

        // Olayların çok hızlı gerçekleşmesi (race condition) durumunda animasyonun ekranda
        // aniden kaybolmasını (flicker) önlemek için minimum akıcı geçiş süresi garantisi
        int elapsed = (int)sw.ElapsedMilliseconds;
        if (elapsed < 350)
        {
            await Task.Delay(350 - elapsed);
        }

        // ── Back on the UI thread: fire the event to trigger navigation ──
        RecordingFinished?.Invoke(projectDir);
    }
}
