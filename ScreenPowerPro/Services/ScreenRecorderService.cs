using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using ScreenPowerPro.Helpers;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Services;

public enum RecordingMode
{
    FullScreen,
    Window,
    Region
}

public class ScreenRecorderService : IDisposable
{
    private readonly SettingsService _settingsService;
    private Process? _ffmpegProcess;
#pragma warning disable CS0618
    private WasapiLoopbackCapture? _loopbackCapture;
    private WaveFileWriter? _loopbackWriter;
    private WaveIn? _micCapture;
    private WaveFileWriter? _micWriter;
#pragma warning restore CS0618

    private System.Timers.Timer? _durationTimer;
    private Stopwatch? _recordStopwatch;

    public bool IsRecording { get; private set; }
    public double ElapsedSeconds => _recordStopwatch?.Elapsed.TotalSeconds ?? 0;

    public event Action<double>? DurationUpdated;
    public event Action? RecordingStarted;
    public event Action<string>? RecordingStopped; // passes projectDir

    private string? _currentProjectDir;
    private string? _currentVideoPath;
    private string? _currentMicPath;
    private string? _currentSystemAudioPath;

    public ScreenRecorderService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<string> StartRecordingAsync(
        string projectDir,
        RecordingMode mode = RecordingMode.FullScreen,
        IntPtr? targetWindowHandle = null,
        int cropX = 0,
        int cropY = 0,
        int cropWidth = 1920,
        int cropHeight = 1080)
    {
        if (IsRecording) throw new InvalidOperationException("Zaten bir kayıt devam ediyor.");

        _currentProjectDir = projectDir;
        string recordingFolder = Path.Combine(projectDir, "recording");
        _currentVideoPath = Path.Combine(recordingFolder, "display-0.mp4");
        _currentMicPath = Path.Combine(recordingFolder, "microphone-0.wav");
        _currentSystemAudioPath = Path.Combine(recordingFolder, "system_audio-0.wav");

        var settings = _settingsService.Current;

        // 1. Hide Desktop Icons / Taskbar if enabled in settings
        if (settings.HideDesktopIcons)
        {
            Win32Helper.SetDesktopIconsVisible(false);
        }
        if (settings.HideTaskbar)
        {
            Win32Helper.SetTaskbarVisible(false);
        }

        // 2. Start Microphone audio recording if enabled
        if (settings.MicAudioEnabled)
        {
            try
            {
#pragma warning disable CS0618
                _micCapture = new WaveIn
                {
                    WaveFormat = new WaveFormat(44100, 1) // 44.1kHz mono
                };
#pragma warning restore CS0618
                _micWriter = new WaveFileWriter(_currentMicPath, _micCapture.WaveFormat);
                _micCapture.DataAvailable += (s, e) =>
                {
                    _micWriter?.Write(e.Buffer, 0, e.BytesRecorded);
                };
                _micCapture.StartRecording();
            }
            catch { }
        }

        // 3. Start System Audio loopback recording if enabled
        if (settings.SystemAudioEnabled)
        {
            try
            {
                _loopbackCapture = new WasapiLoopbackCapture();
                _loopbackWriter = new WaveFileWriter(_currentSystemAudioPath, _loopbackCapture.WaveFormat);
                _loopbackCapture.DataAvailable += (s, e) =>
                {
                    _loopbackWriter?.Write(e.Buffer, 0, e.BytesRecorded);
                };
                _loopbackCapture.StartRecording();
            }
            catch { }
        }

        // 4. Start FFmpeg Screen Grabber
        string ffmpegExe = FFmpegHelper.FindFFmpeg();
        int drawMouse = settings.HideMouseCursor ? 0 : 1;
        int fps = settings.Fps > 0 ? settings.Fps : 60;

        string videoInputArgs;
        if (mode == RecordingMode.Window && targetWindowHandle.HasValue && targetWindowHandle.Value != IntPtr.Zero)
        {
            // Capture specific window by title or gdigrab
            var sb = new System.Text.StringBuilder(256);
            Win32Helper.GetWindowText(targetWindowHandle.Value, sb, 256);
            string title = sb.ToString();
            videoInputArgs = $"-f gdigrab -draw_mouse {drawMouse} -framerate {fps} -i title=\"{title}\"";
        }
        else if (mode == RecordingMode.Region)
        {
            videoInputArgs = $"-f gdigrab -draw_mouse {drawMouse} -framerate {fps} -offset_x {cropX} -offset_y {cropY} -video_size {cropWidth}x{cropHeight} -i desktop";
        }
        else
        {
            // Full desktop (Direct3D Desktop Duplication if supported, or gdigrab fallback)
            videoInputArgs = $"-f gdigrab -draw_mouse {drawMouse} -framerate {fps} -i desktop";
        }

        string fullFfmpegArgs = $"-y {videoInputArgs} -c:v libx264 -preset ultrafast -tune zerolatency -crf 18 -pix_fmt yuv420p \"{_currentVideoPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegExe,
            Arguments = fullFfmpegArgs,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _ffmpegProcess = new Process { StartInfo = psi };
        _ffmpegProcess.Start();

        // 5. Start Elapsed Stopwatch & Timer
        _recordStopwatch = Stopwatch.StartNew();
        _durationTimer = new System.Timers.Timer(500);
        _durationTimer.Elapsed += (s, e) =>
        {
            DurationUpdated?.Invoke(ElapsedSeconds);
        };
        _durationTimer.Start();

        IsRecording = true;
        RecordingStarted?.Invoke();

        return _currentVideoPath;
    }

    public async Task StopRecordingAsync()
    {
        if (!IsRecording) return;

        IsRecording = false;
        _durationTimer?.Stop();
        _durationTimer?.Dispose();
        _recordStopwatch?.Stop();

        // Stop Audio
        try
        {
            if (_micCapture != null)
            {
                _micCapture.StopRecording();
                _micCapture.Dispose();
                _micCapture = null;
            }
            if (_micWriter != null)
            {
                _micWriter.Dispose();
                _micWriter = null;
            }
        }
        catch { }

        try
        {
            if (_loopbackCapture != null)
            {
                _loopbackCapture.StopRecording();
                _loopbackCapture.Dispose();
                _loopbackCapture = null;
            }
            if (_loopbackWriter != null)
            {
                _loopbackWriter.Dispose();
                _loopbackWriter = null;
            }
        }
        catch { }

        // Gracefully Stop FFmpeg by writing 'q'
        if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
        {
            try
            {
                _ffmpegProcess.StandardInput.WriteLine("q");
                await Task.Run(() => _ffmpegProcess.WaitForExit(4000));
                if (!_ffmpegProcess.HasExited)
                {
                    _ffmpegProcess.Kill();
                }
            }
            catch { }
            finally
            {
                _ffmpegProcess.Dispose();
                _ffmpegProcess = null;
            }
        }

        // Restore Desktop Icons & Taskbar
        Win32Helper.SetDesktopIconsVisible(true);
        Win32Helper.SetTaskbarVisible(true);

        if (!string.IsNullOrEmpty(_currentProjectDir))
        {
            RecordingStopped?.Invoke(_currentProjectDir);
        }
    }

    public void Dispose()
    {
        if (IsRecording)
        {
            StopRecordingAsync().Wait(2000);
        }
        GC.SuppressFinalize(this);
    }
}
