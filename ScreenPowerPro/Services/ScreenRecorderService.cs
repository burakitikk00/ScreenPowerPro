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
using ScreenPowerPro.Core.Capture;

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

    private WindowCaptureService? _windowCaptureService;

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


        // Bounds Checking for Crop (Sınırları aşmayı önleme)
        if (mode == RecordingMode.Region)
        {
            int screenW = Win32Helper.GetSystemMetrics(Win32Helper.SM_CXSCREEN);
            int screenH = Win32Helper.GetSystemMetrics(Win32Helper.SM_CYSCREEN);
            if (screenW <= 0) screenW = 1920;
            if (screenH <= 0) screenH = 1080;

            cropX = Math.Clamp(cropX, 0, Math.Max(0, screenW - 32));
            cropY = Math.Clamp(cropY, 0, Math.Max(0, screenH - 32));
            cropWidth = Math.Clamp(cropWidth, 32, screenW - cropX);
            cropHeight = Math.Clamp(cropHeight, 32, screenH - cropY);

            if (cropWidth % 2 != 0) cropWidth--;
            if (cropHeight % 2 != 0) cropHeight--;
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
                        try
                        {
                            _micWriter?.Write(buffer.ToArray(), 0, buffer.Length);
                        }
                        catch { }
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
                        _legacyMicCapture.DataAvailable += (s, e) =>
                        {
                            try
                            {
                                _micWriter?.Write(e.Buffer, 0, e.BytesRecorded);
                            }
                            catch { }
                        };
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
                        try
                        {
                            _loopbackWriter?.Write(buffer.ToArray(), 0, buffer.Length);
                        }
                        catch { }
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
                        _legacyLoopbackCapture.DataAvailable += (s, e) =>
                        {
                            try
                            {
                                _loopbackWriter?.Write(e.Buffer, 0, e.BytesRecorded);
                            }
                            catch { }
                        };
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
            bool isWgcMode = false;
            if (mode == RecordingMode.Window && targetWindowHandle.HasValue && targetWindowHandle.Value != IntPtr.Zero)
            {
                // WGC (Windows Graphics Capture) ile Uygulama Yakalama
                try
                {
                    _windowCaptureService = new WindowCaptureService();
                    var (w, h) = _windowCaptureService.PrepareCapture(targetWindowHandle.Value);
                    if (w <= 0 || h <= 0)
                    {
                        Win32Helper.GetWindowRect(targetWindowHandle.Value, out var rect);
                        w = rect.Width > 0 ? rect.Width : 1920;
                        h = rect.Height > 0 ? rect.Height : 1080;
                    }

                    // FFmpeg'e RAW BGRA stream basacağız
                    videoInputArgs = $"-f rawvideo -pixel_format bgra -video_size {w}x{h} -framerate {fps} -i -";
                    isWgcMode = true;
                }
                catch
                {
                    _windowCaptureService?.Dispose();
                    _windowCaptureService = null;
                    isWgcMode = false;
                    videoInputArgs = $"-f gdigrab -draw_mouse {drawMouse} -framerate {fps} -i desktop";
                }
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


            int crf = settings.RecordingQuality switch
            {
                "Ultra" => 15,
                "High" => 18,
                "Medium" => 23,
                "Low" => 28,
                _ => 18
            };
            // Standart MP4 çıkışı (MediaFoundation ile tam uyumlu)
            // scale=trunc(iw/2)*2:trunc(ih/2)*2 prevents x264 crash when window dimensions are odd.
            // -profile:v baseline -level 3.1: Windows MF hardware H.264 decoder ile tam uyumlu.
            // -tune zerolatency kaldırıldı: WMF ile uyumsuz SEI/B-frame yapısı üretiyordu.
            string vfFilter = "-vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\"";

            // Encoder seçimi: h264_mf (Windows MF) > h264_nvenc (NVIDIA) > libx264 (fallback)
            // h264_nvenc ve h264_mf ile encode edilen videolar Windows MF tarafından garantili decode edilir.
            string videoEncodeArgs = BuildVideoEncodeArgs(ffmpegExe, crf);

            // WinUI 3 MediaPlayerElement (unpackaged) BUG FIX:
            // MediaPlayerElement fails with 0xC00D36FA (SourceNotSupported) if the MP4 file
            // only contains a video stream and lacks an audio stream.
            // We use lavfi anullsrc to mix a silent audio track into the recording.
            string dummyAudioInput = "-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=44100";
            string audioEncodeArgs = "-c:a aac -shortest";

            string fullFfmpegArgs = $"-y {videoInputArgs} {dummyAudioInput} {vfFilter} {videoEncodeArgs} {audioEncodeArgs} -movflags +faststart \"{_currentVideoPath}\"";

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
            // Drain standard error so the Windows pipe buffer never fills up and deadlocks FFmpeg
            _ffmpegProcess.ErrorDataReceived += (s, e) => { };
            _ffmpegProcess.Start();
            _ffmpegProcess.BeginErrorReadLine();

            if (isWgcMode && _windowCaptureService != null)
            {
                bool captureCursor = !settings.HideMouseCursor;
                _windowCaptureService.StartCapture(_ffmpegProcess.StandardInput.BaseStream, captureCursor);
            }
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

    /// <summary>
    /// Selects the best available H.264 encoder in priority order:
    ///   1. h264_mf     (Windows Media Foundation MFT - guaranteed WMF decode)
    ///   2. h264_nvenc  (NVIDIA GPU - WMF compatible, high quality)
    ///   3. libx264     (software fallback)
    /// h264_mf is first priority because it is guaranteed to produce output
    /// decodeable by Windows Media Foundation (same encoder = same decoder).
    /// </summary>
    private static string BuildVideoEncodeArgs(string ffmpegExe, int crf)
    {
        // Yüksek kaliteli ekran kaydı için bitrate (CRF üzerinden haritalanır)
        // Ekran kayıtlarında zamanla pikselleşmeyi önlemek için yüksek bitrate ve keyframe (GOP) interval kullanıyoruz.
        int bitrate = crf switch { <= 15 => 15000, <= 18 => 10000, <= 23 => 5000, _ => 3000 };
        string gop = "-g 120"; // Her 120 karede bir tam kare (keyframe) at, 60fps'de 2 saniyeye denk gelir

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                Arguments = "-encoders",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(3000);

            if (output.Contains("h264_mf"))
            {
                // Windows Media Foundation hardware encode — guaranteed WMF decode
                // Same encoder as the WinUI3 MediaPlayer decoder path.
                System.Diagnostics.Debug.WriteLine($"[Recorder] Encoder: h264_mf ({bitrate}k)");
                return $"-c:v h264_mf -rate_control pc_vbr -b:v {bitrate}k {gop} -pix_fmt yuv420p";
            }

            if (output.Contains("h264_nvenc"))
            {
                // NVIDIA hardware encode — WMF compatible output
                System.Diagnostics.Debug.WriteLine($"[Recorder] Encoder: h264_nvenc ({bitrate}k)");
                return $"-c:v h264_nvenc -preset p1 -b:v {bitrate}k -maxrate {bitrate + 5000}k {gop} -bufsize {bitrate * 2}k -pix_fmt yuv420p";
            }
        }
        catch { }

        // Fallback: libx264 software encode
        System.Diagnostics.Debug.WriteLine($"[Recorder] Encoder: libx264 (crf={crf})");
        return $"-c:v libx264 -preset ultrafast -profile:v baseline -level 3.1 -crf {crf} {gop} -pix_fmt yuv420p";
    }

    public async Task StopRecordingAsync()
    {
        if (!IsRecording) return;

        IsRecording = false;
        _durationTimer?.Stop();
        _durationTimer?.Dispose();
        _recordStopwatch?.Stop();

        // Tüm ağır durdurma, flushing ve FFmpeg kapanış işlemlerini arka plana alarak UI thread'i dondurmasını önlüyoruz
        await Task.Run(async () =>
        {
            // 1. Mikrofon Kaydını Durdur
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

            // 2. Sistem Ses Kaydını Durdur
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

            // 3. Boş veya sadece başlık içeren (<= 200 bayt) ses dosyalarını temizle
            try
            {
                if (!string.IsNullOrEmpty(_currentSystemAudioPath) && File.Exists(_currentSystemAudioPath))
                {
                    var fi = new FileInfo(_currentSystemAudioPath);
                    if (fi.Length <= 200)
                    {
                        File.Delete(_currentSystemAudioPath);
                        _currentSystemAudioPath = null;
                    }
                }
            }
            catch { }

            try
            {
                if (!string.IsNullOrEmpty(_currentMicPath) && File.Exists(_currentMicPath))
                {
                    var fi = new FileInfo(_currentMicPath);
                    if (fi.Length <= 200)
                    {
                        File.Delete(_currentMicPath);
                        _currentMicPath = null;
                    }
                }
            }
            catch { }

            // 4. Stop capture services feeding FFmpeg FIRST to prevent corrupting the input pipe
            if (_windowCaptureService != null)
            {
                _windowCaptureService.StopCapture();
                _windowCaptureService.Dispose();
                _windowCaptureService = null;
            }

            // 5. FFmpeg'i Graceful olarak sonlandır ('q' gönder, stdin kapat ve asenkron bekle)
            if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
            {
                try
                {
                    try
                    {
                        await _ffmpegProcess.StandardInput.WriteLineAsync("q");
                        await _ffmpegProcess.StandardInput.FlushAsync();
                    }
                    catch { }

                    try
                    {
                        _ffmpegProcess.StandardInput.Close();
                    }
                    catch { }

                    // FFmpeg'in arabellekteki kareleri işlemesi ve mp4 dosyasını tamamlaması (moov atom) için
                    // tamamen bitmesini bekliyoruz. Timeout uygulanmıyor ki uzun kayıtlarda video eksik kalmasın.
                    await _ffmpegProcess.WaitForExitAsync();
                }
                catch { }
                finally
                {
                    _ffmpegProcess?.Dispose();
                    _ffmpegProcess = null;
                }
            }
        });

        if (!string.IsNullOrEmpty(_currentProjectDir))
        {
            RecordingStopped?.Invoke(_currentProjectDir);
        }
    }



    public void Dispose()
    {
        if (IsRecording)
        {
            // .Wait() UI thread'ini bloke eder — fire-and-forget yeterli
            _ = StopRecordingAsync();
        }
        GC.SuppressFinalize(this);
    }
}
