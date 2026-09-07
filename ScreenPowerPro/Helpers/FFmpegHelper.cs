using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ScreenPowerPro.Models;
using ScreenPowerPro.Services;

namespace ScreenPowerPro.Helpers;

/// <summary>
/// FFmpeg yürütülebilir dosyasını bulma ve render komutlarını oluşturma yardımcısı.
/// </summary>
public static class FFmpegHelper
{
    private static string? _cachedFfmpegPath;

    /// <summary>
    /// Sistemde veya yaygın dizinlerde yüklü FFmpeg.exe dosyasını tespit eder.
    /// </summary>
    public static string FindFFmpeg()
    {
        if (!string.IsNullOrEmpty(_cachedFfmpegPath) && File.Exists(_cachedFfmpegPath))
            return _cachedFfmpegPath;

        // Olası FFmpeg kurulum yolları
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Packages\Gyan.FFmpeg.Essentials_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.1.1-essentials_build\bin\ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\ffmpeg\bin\ffmpeg.exe"),
            @"C:\ffmpeg\bin\ffmpeg.exe",
            "ffmpeg.exe" // Sistem PATH ortam değişkeninde
        ];

        foreach (var p in candidates)
        {
            if (File.Exists(p))
            {
                _cachedFfmpegPath = p;
                return p;
            }
        }

        // WinGet paketleri klasörünü derinlemesine ara
        string wingetPkgs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Packages");
        if (Directory.Exists(wingetPkgs))
        {
            var files = Directory.GetFiles(wingetPkgs, "ffmpeg.exe", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                _cachedFfmpegPath = files[0];
                return files[0];
            }
        }

        _cachedFfmpegPath = "ffmpeg.exe";
        return _cachedFfmpegPath;
    }

    /// <summary>
    /// Proje manifestosu, zoom efektleri ve ses kanallarını birleştiren
    /// tam uyumlu FFmpeg video işleme CLI argümanlarını üretir.
    /// </summary>
    public static string BuildRenderCommand(
        ProjectManifest manifest,
        string outputPath,
        string? projectDir = null,
        int targetWidth = 1920,
        int targetHeight = 1080,
        int targetFps = 60)
    {
        var sb = new StringBuilder();

        // 1. Dosya yollarını mutlak yola çözümle (göreceli yollar için proje dizini referans alınır)
        string videoPath = manifest.VideoPath;
        if (!Path.IsPathRooted(videoPath) && !string.IsNullOrEmpty(projectDir))
        {
            videoPath = Path.GetFullPath(Path.Combine(projectDir, videoPath.TrimStart('.', '/', '\\')));
        }

        string? micPath = manifest.MicAudioPath;
        if (!string.IsNullOrEmpty(micPath) && !Path.IsPathRooted(micPath) && !string.IsNullOrEmpty(projectDir))
        {
            micPath = Path.GetFullPath(Path.Combine(projectDir, micPath.TrimStart('.', '/', '\\')));
        }

        string? sysPath = manifest.SystemAudioPath;
        if (!string.IsNullOrEmpty(sysPath) && !Path.IsPathRooted(sysPath) && !string.IsNullOrEmpty(projectDir))
        {
            sysPath = Path.GetFullPath(Path.Combine(projectDir, sysPath.TrimStart('.', '/', '\\')));
        }

        // 2. Girdi dosyaları
        sb.Append($"-y -i \"{videoPath}\" ");
        bool hasMic = !string.IsNullOrEmpty(micPath) && File.Exists(micPath);
        bool hasSys = !string.IsNullOrEmpty(sysPath) && File.Exists(sysPath);

        if (hasMic)
        {
            sb.Append($"-i \"{micPath}\" ");
        }
        if (hasSys)
        {
            sb.Append($"-i \"{sysPath}\" ");
        }

        // 3. Zoom ve Pan video filtre zinciri (ZoomEngineService kullanarak)
        var filterComplex = new StringBuilder();
        var zoomEffects = manifest.Timeline.ZoomEffects;

        if (zoomEffects.Count > 0)
        {
            var zoomEngine = new ZoomEngineService();
            string zoompanFilter = zoomEngine.BuildZoompanFilter(zoomEffects, targetWidth, targetHeight, targetFps);
            filterComplex.Append($"[0:v]{zoompanFilter}[vfiltered]");
        }
        else
        {
            filterComplex.Append($"[0:v]scale={targetWidth}:{targetHeight}[vfiltered]");
        }

        // 4. Ses miksajı
        if (hasMic && hasSys)
        {
            filterComplex.Append(";[1:a][2:a]amix=inputs=2:duration=first[aout]");
            sb.Append($"-filter_complex \"{filterComplex}\" -map \"[vfiltered]\" -map \"[aout]\" ");
        }
        else if (hasMic)
        {
            sb.Append($"-filter_complex \"{filterComplex}\" -map \"[vfiltered]\" -map 1:a ");
        }
        else if (hasSys)
        {
            sb.Append($"-filter_complex \"{filterComplex}\" -map \"[vfiltered]\" -map 1:a ");
        }
        else
        {
            sb.Append($"-filter_complex \"{filterComplex}\" -map \"[vfiltered]\" ");
        }

        // 5. Video codec & bitiş parametreleri
        sb.Append($"-c:v libx264 -preset faster -crf 18 -pix_fmt yuv420p -r {targetFps} -c:a aac -b:a 192k \"{outputPath}\"");

        return sb.ToString();
    }

    /// <summary>
    /// FFprobe veya FFmpeg kullanarak video dosyasının süresini (saniye cinsinden) okur.
    /// </summary>
    public static double GetVideoDuration(string videoPath)
    {
        try
        {
            if (!File.Exists(videoPath)) return 0;
            string ffmpegExe = FindFFmpeg();
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffmpegExe,
                Arguments = $"-i \"{videoPath}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return 0;
            string output = proc.StandardError.ReadToEnd();
            proc.WaitForExit(3000);

            // "Duration: 00:01:23.45"
            int durIdx = output.IndexOf("Duration: ", StringComparison.OrdinalIgnoreCase);
            if (durIdx != -1)
            {
                string part = output.Substring(durIdx + 10, 11);
                if (TimeSpan.TryParse(part, out var ts))
                {
                    return ts.TotalSeconds;
                }
            }
        }
        catch { }
        return 0;
    }
}
