using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ScreenPowerPro.Helpers;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Services;

public class ExportService
{
    private static readonly Regex TimeRegex = new(@"time=(\d+):(\d+):(\d+\.\d+)", RegexOptions.Compiled);
    private Process? _currentProcess;

    public event Action<double>? ProgressChanged; // 0.0 to 100.0
    public event Action<string>? ExportCompleted;
    public event Action<string>? ExportFailed;

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

        string ffmpegPath = FFmpegHelper.FindFFmpeg();
        string args = FFmpegHelper.BuildRenderCommand(manifest, outputPath, projectDir, targetWidth, targetHeight, targetFps);

        double totalDuration = manifest.Metadata.DurationSeconds > 0 ? manifest.Metadata.DurationSeconds : 10.0;

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            WorkingDirectory = !string.IsNullOrEmpty(projectDir) ? projectDir : AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _currentProcess = new Process { StartInfo = psi };

        _currentProcess.ErrorDataReceived += (s, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            var match = TimeRegex.Match(e.Data);
            if (match.Success)
            {
                int hours = int.Parse(match.Groups[1].Value);
                int minutes = int.Parse(match.Groups[2].Value);
                double seconds = double.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);

                double currentSec = hours * 3600 + minutes * 60 + seconds;
                double percent = Math.Min(100.0, (currentSec / totalDuration) * 100.0);
                ProgressChanged?.Invoke(percent);
            }
        };

        try
        {
            _currentProcess.Start();
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

            if (_currentProcess.ExitCode == 0)
            {
                ProgressChanged?.Invoke(100.0);
                ExportCompleted?.Invoke(outputPath);
                return outputPath;
            }
            else
            {
                string err = $"Export FFmpeg çıkış kodu: {_currentProcess.ExitCode}";
                ExportFailed?.Invoke(err);
                throw new Exception(err);
            }
        }
        finally
        {
            _currentProcess?.Dispose();
            _currentProcess = null;
        }
    }

    public void CancelExport()
    {
        try
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                _currentProcess.Kill();
            }
        }
        catch { }
    }
}
