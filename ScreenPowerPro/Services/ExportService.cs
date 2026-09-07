using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ScreenPowerPro.Helpers;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Services;

public class ExportProgressReport
{
    public double ProgressPercent { get; set; }
    public double CurrentSeconds { get; set; }
    public double TotalSeconds { get; set; }
    public double RemainingSeconds { get; set; }
    public double Speed { get; set; }
    public string FormattedRemainingTime { get; set; } = "Hesaplanıyor...";
}

public class ExportService
{
    private readonly CursorRenderService _cursorRenderService;
    private Process? _currentProcess;

    public event Action<double>? ProgressChanged; // 0.0 to 100.0
    public event Action<ExportProgressReport>? ProgressUpdated;
    public event Action<string>? ExportCompleted;
    public event Action<string>? ExportFailed;

    public ExportService(CursorRenderService cursorRenderService)
    {
        _cursorRenderService = cursorRenderService;
    }

    public async Task<string> ExportVideoAsync(
        ProjectManifest manifest,
        string outputPath,
        string? projectDir = null,
        int targetWidth = 1920,
        int targetHeight = 1080,
        int targetFps = 60,
        CancellationToken cancellationToken = default)
    {
        string? outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        // 1. Toplam Süreyi (TotalDuration) klip segmentleri ve FFprobe ile kesin olarak hesapla
        double totalDuration = 0;
        if (manifest.Timeline?.VideoTrack?.Clips != null && manifest.Timeline.VideoTrack.Clips.Count > 0)
        {
            totalDuration = manifest.Timeline.VideoTrack.Clips.Sum(c => Math.Max(0, c.SourceEnd - c.SourceStart));
        }

        string resolvedVid = FFmpegHelper.ResolveMediaPath(projectDir, manifest.VideoPath, "display-0.mp4");
        if (totalDuration <= 0 && File.Exists(resolvedVid))
        {
            totalDuration = FFmpegHelper.GetVideoDuration(resolvedVid);
        }

        if (totalDuration <= 0 && manifest.Metadata?.DurationSeconds > 0)
        {
            totalDuration = manifest.Metadata.DurationSeconds;
        }

        if (totalDuration <= 0)
        {
            totalDuration = 10.0;
        }

        // 2. Sanal İmleç Katmanını (.mov) Render Et
        string? cursorOverlayPath = null;
        try
        {
            cursorOverlayPath = await _cursorRenderService.RenderCursorOverlayAsync(
                projectDir ?? AppContext.BaseDirectory,
                manifest,
                targetWidth,
                targetHeight,
                targetFps,
                totalDuration,
                cancellationToken,
                (cursorPct) =>
                {
                    double overallPct = Math.Clamp(cursorPct * 0.15, 0.0, 15.0);
                    var report = new ExportProgressReport
                    {
                        ProgressPercent = overallPct,
                        CurrentSeconds = 0,
                        TotalSeconds = totalDuration,
                        RemainingSeconds = -1,
                        Speed = 1.0,
                        FormattedRemainingTime = "İmleç katmanı hazırlanıyor..."
                    };
                    ProgressChanged?.Invoke(overallPct);
                    ProgressUpdated?.Invoke(report);
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExportService] Sanal imleç katmanı oluşturma uyarısı: {ex.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        string ffmpegPath = FFmpegHelper.FindFFmpeg();
        string args = FFmpegHelper.BuildRenderCommand(manifest, outputPath, projectDir, targetWidth, targetHeight, targetFps, cursorOverlayPath);

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            WorkingDirectory = !string.IsNullOrEmpty(projectDir) ? projectDir : AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _currentProcess = new Process { StartInfo = psi };

        var stderrBuffer = new ConcurrentQueue<string>();
        double currentSec = 0;
        double speed = 1.0;
        var stopwatch = Stopwatch.StartNew();
        double lastReportedPercent = cursorOverlayPath != null ? 15.0 : 0.0;

        _currentProcess.OutputDataReceived += (s, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            string line = e.Data.Trim();

            if (line.StartsWith("out_time_us="))
            {
                if (long.TryParse(line.AsSpan(12), out long us) && us >= 0)
                {
                    currentSec = us / 1000000.0;
                }
            }
            else if (line.StartsWith("out_time="))
            {
                string tStr = line.Substring(9).Trim();
                if (TimeSpan.TryParse(tStr, CultureInfo.InvariantCulture, out var ts))
                {
                    currentSec = ts.TotalSeconds;
                }
            }
            else if (line.StartsWith("speed="))
            {
                string spStr = line.Substring(6).Replace("x", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (double.TryParse(spStr, CultureInfo.InvariantCulture, out double sp) && sp > 0)
                {
                    speed = sp;
                }
            }
            else if (line.StartsWith("progress="))
            {
                bool isEnd = line.Equals("progress=end", StringComparison.OrdinalIgnoreCase);
                double rawPercent = (currentSec / totalDuration) * 100.0;

                double percent;
                if (isEnd)
                {
                    percent = 100.0;
                }
                else if (cursorOverlayPath != null)
                {
                    percent = Math.Clamp(15.0 + (rawPercent * 0.84), 15.0, 99.0);
                }
                else
                {
                    percent = Math.Clamp(rawPercent, 0.0, 99.0);
                }

                if (percent < lastReportedPercent && !isEnd)
                {
                    percent = lastReportedPercent;
                }
                lastReportedPercent = percent;

                double elapsed = stopwatch.Elapsed.TotalSeconds;
                double remainingSec = -1;

                if (!isEnd && percent > 0.5 && elapsed > 0.5)
                {
                    double timeBasedRemaining = (elapsed / (percent / 100.0)) - elapsed;

                    if (speed > 0.1)
                    {
                        double speedBasedRemaining = Math.Max(0, totalDuration - currentSec) / speed;
                        remainingSec = Math.Max(0, (timeBasedRemaining * 0.5) + (speedBasedRemaining * 0.5));
                    }
                    else
                    {
                        remainingSec = Math.Max(0, timeBasedRemaining);
                    }
                }

                string formattedTime;
                if (isEnd)
                {
                    formattedTime = "00:00";
                }
                else if (remainingSec < 0)
                {
                    formattedTime = "Hesaplanıyor...";
                }
                else if (remainingSec < 4)
                {
                    formattedTime = "Birkaç saniye...";
                }
                else if (remainingSec < 60)
                {
                    formattedTime = $"{Math.Ceiling(remainingSec)} sn";
                }
                else
                {
                    var rTs = TimeSpan.FromSeconds(remainingSec);
                    formattedTime = rTs.TotalHours >= 1
                        ? $"{(int)rTs.TotalHours:D2}:{rTs.Minutes:D2}:{rTs.Seconds:D2}"
                        : $"{rTs.Minutes:D2}:{rTs.Seconds:D2}";
                }

                var report = new ExportProgressReport
                {
                    ProgressPercent = percent,
                    CurrentSeconds = currentSec,
                    TotalSeconds = totalDuration,
                    RemainingSeconds = isEnd ? 0 : remainingSec,
                    Speed = speed,
                    FormattedRemainingTime = formattedTime
                };

                ProgressChanged?.Invoke(percent);
                ProgressUpdated?.Invoke(report);
            }
        };

        _currentProcess.ErrorDataReceived += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            stderrBuffer.Enqueue(e.Data);
            while (stderrBuffer.Count > 30)
            {
                stderrBuffer.TryDequeue(out _);
            }
        };

        try
        {
            _currentProcess.Start();
            _currentProcess.BeginOutputReadLine();
            _currentProcess.BeginErrorReadLine();

            using var reg = cancellationToken.Register(() =>
            {
                try
                {
                    if (_currentProcess != null && !_currentProcess.HasExited)
                    {
                        _currentProcess.Kill();
                    }
                }
                catch { }
            });

            await _currentProcess.WaitForExitAsync(cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (_currentProcess.ExitCode == 0)
            {
                var finalReport = new ExportProgressReport
                {
                    ProgressPercent = 100.0,
                    CurrentSeconds = totalDuration,
                    TotalSeconds = totalDuration,
                    RemainingSeconds = 0,
                    Speed = speed,
                    FormattedRemainingTime = "00:00"
                };
                ProgressChanged?.Invoke(100.0);
                ProgressUpdated?.Invoke(finalReport);
                ExportCompleted?.Invoke(outputPath);
                return outputPath;
            }
            else
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                string errLog = string.Join(" | ", stderrBuffer);
                string err = !string.IsNullOrWhiteSpace(errLog)
                    ? errLog
                    : $"FFmpeg çıkış kodu: {_currentProcess.ExitCode}";
                ExportFailed?.Invoke(err);
                throw new Exception(err);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            _currentProcess?.Dispose();
            _currentProcess = null;

            // Geçici imleç katmanı dosyasını temizle
            try
            {
                if (!string.IsNullOrEmpty(cursorOverlayPath) && File.Exists(cursorOverlayPath))
                {
                    File.Delete(cursorOverlayPath);
                }
            }
            catch { }
        }
    }

    public void CancelExport()
    {
        try
        {
            _cursorRenderService.Cancel();
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                _currentProcess.Kill();
            }
        }
        catch { }
    }
}
