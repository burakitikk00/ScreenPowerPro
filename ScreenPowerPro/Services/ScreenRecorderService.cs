using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using NAudio.CoreAudioApi;
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
    private WasapiLoopbackCapture? _legacyLoopbackCapture;
    private WaveIn? _legacyMicCapture;
#pragma warning restore CS0618
    private WasapiRecorder? _loopbackRecorder;
    private WasapiRecorder? _micRecorder;
    private WaveFileWriter? _loopbackWriter;
    private WaveFileWriter? _micWriter;

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

        await Task.Run(() =>
        {
            // 2. Start Microphone audio recording if enabled
            if (settings.MicAudioEnabled)
            {
                try
                {
                    var micBuilder = new WasapiRecorderBuilder();
                    if (!string.IsNullOrEmpty(settings.SelectedMicDevice))
                    {
                        using var enumerator = new MMDeviceEnumerator();
                        var dev = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                            .FirstOrDefault(d => d.ID == settings.SelectedMicDevice || d.FriendlyName == settings.SelectedMicDevice);
                        if (dev != null) micBuilder.WithDevice(dev);
                    }
                    _micRecorder = micBuilder.Build();
                    _micWriter = new WaveFileWriter(_currentMicPath, _micRecorder.WaveFormat);
                    _micRecorder.DataAvailable += (buffer, flags, dev, qpc) =>
                    {
                        _micWriter?.Write(buffer.ToArray(), 0, buffer.Length);
                    };
                    _micRecorder.StartRecording();
                }
                catch
                {
                    // Fallback to WaveIn
                    try
                    {
                        _legacyMicCapture = new WaveIn { WaveFormat = new WaveFormat(44100, 1) };
                        _micWriter = new WaveFileWriter(_currentMicPath, _legacyMicCapture.WaveFormat);
                        _legacyMicCapture.DataAvailable += (s, e) => _micWriter?.Write(e.Buffer, 0, e.BytesRecorded);
                        _legacyMicCapture.StartRecording();
                    }
                    catch { }
                }
            }

            // 3. Start System Audio loopback recording if enabled
            if (settings.SystemAudioEnabled)
            {
                try
                {
                    if (settings.OnlyAppAudioEnabled && settings.SelectedAppAudioProcesses != null && settings.SelectedAppAudioProcesses.Count > 0)
                    {
                        uint targetPid = 0;
                        foreach (var pStr in settings.SelectedAppAudioProcesses)
                        {
                            if (uint.TryParse(pStr, out uint pid))
                            {
                                targetPid = pid;
                                break;
                            }
                            var p = Process.GetProcessesByName(pStr).FirstOrDefault();
                            if (p != null)
                            {
                                targetPid = (uint)p.Id;
                                break;
                            }
                        }

                        if (targetPid > 0)
                        {
#pragma warning disable CA1416
                            var procBuilder = new WasapiRecorderBuilder()
                                .WithProcessLoopback(targetPid, ProcessLoopbackMode.IncludeTargetProcessTree);
                            _loopbackRecorder = procBuilder.BuildAsync().GetAwaiter().GetResult();
#pragma warning restore CA1416
                        }
                    }

                    if (_loopbackRecorder == null)
                    {
                        var loopBuilder = new WasapiRecorderBuilder().WithLoopbackCapture();
                        if (!string.IsNullOrEmpty(settings.SelectedSpeakerDevice))
                        {
                            using var enumerator = new MMDeviceEnumerator();
                            var dev = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                                .FirstOrDefault(d => d.ID == settings.SelectedSpeakerDevice || d.FriendlyName == settings.SelectedSpeakerDevice);
                            if (dev != null) loopBuilder.WithDevice(dev);
                        }
                        _loopbackRecorder = loopBuilder.Build();
                    }

                    _loopbackWriter = new WaveFileWriter(_currentSystemAudioPath, _loopbackRecorder.WaveFormat);
                    _loopbackRecorder.DataAvailable += (buffer, flags, dev, qpc) =>
                    {
                        _loopbackWriter?.Write(buffer.ToArray(), 0, buffer.Length);
                    };
                    _loopbackRecorder.StartRecording();
                }
                catch
                {
                    // Fallback to legacy WasapiLoopbackCapture
                    try
                    {
#pragma warning disable CS0618
                        _legacyLoopbackCapture = new WasapiLoopbackCapture();
#pragma warning restore CS0618
                        _loopbackWriter = new WaveFileWriter(_currentSystemAudioPath, _legacyLoopbackCapture.WaveFormat);
                        _legacyLoopbackCapture.DataAvailable += (s, e) => _loopbackWriter?.Write(e.Buffer, 0, e.BytesRecorded);
                        _legacyLoopbackCapture.StartRecording();
                    }
                    catch { }
                }
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
        });

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

        return _currentVideoPath!;
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
            if (_micRecorder != null)
            {
                _micRecorder.StopRecording();
                _micRecorder.Dispose();
                _micRecorder = null;
            }
            if (_legacyMicCapture != null)
            {
                _legacyMicCapture.StopRecording();
                _legacyMicCapture.Dispose();
                _legacyMicCapture = null;
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
            if (_loopbackRecorder != null)
            {
                _loopbackRecorder.StopRecording();
                _loopbackRecorder.Dispose();
                _loopbackRecorder = null;
            }
            if (_legacyLoopbackCapture != null)
            {
                _legacyLoopbackCapture.StopRecording();
                _legacyLoopbackCapture.Dispose();
                _legacyLoopbackCapture = null;
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
                _ffmpegProcess.StandardInput.Write('q');
                _ffmpegProcess.StandardInput.Flush();
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
