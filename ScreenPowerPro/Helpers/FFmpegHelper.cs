using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Packages\Gyan.FFmpeg.Essentials_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.1.1-essentials_build\bin\ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\ffmpeg\bin\ffmpeg.exe"),
            @"C:\ffmpeg\bin\ffmpeg.exe",
            "ffmpeg.exe"
        ];

        foreach (var p in candidates)
        {
            if (File.Exists(p))
            {
                _cachedFfmpegPath = p;
                return p;
            }
        }

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
    /// Medya dosyası yolunu (.mp4, .webm, .wav gibi uzantıları ve göreceli yolları) akıllıca çözümler.
    /// </summary>
    public static string ResolveMediaPath(string? projectDir, string? rawPath, string defaultFileName)
    {
        if (string.IsNullOrEmpty(rawPath) && string.IsNullOrEmpty(projectDir))
            return string.Empty;

        if (!string.IsNullOrEmpty(rawPath) && Path.IsPathRooted(rawPath) && File.Exists(rawPath))
        {
            return rawPath;
        }

        if (!string.IsNullOrEmpty(projectDir))
        {
            if (!string.IsNullOrEmpty(rawPath))
            {
                string trimmed = rawPath.TrimStart('.', '/', '\\');
                string combined = Path.GetFullPath(Path.Combine(projectDir, trimmed));
                if (File.Exists(combined))
                {
                    return combined;
                }
            }

            string defaultPath = Path.Combine(projectDir, "recording", defaultFileName);
            if (File.Exists(defaultPath))
            {
                return defaultPath;
            }

            string baseNoExt = Path.Combine(projectDir, "recording", Path.GetFileNameWithoutExtension(defaultFileName));
            string[] possibleExts = [".webm", ".mp4", ".wav", ".mkv"];
            foreach (var ext in possibleExts)
            {
                string candidate = baseNoExt + ext;
                if (File.Exists(candidate)) return candidate;
            }
        }

        return rawPath ?? string.Empty;
    }

    /// <summary>
    /// Proje manifestosu, zoom efektleri, sanal imleç katmanı, filigran ve ses kanallarını birleştiren
    /// tam uyumlu FFmpeg video işleme CLI argümanlarını üretir.
    /// </summary>
    public static string BuildRenderCommand(
        ProjectManifest manifest,
        string outputPath,
        string? projectDir = null,
        int targetWidth = 1920,
        int targetHeight = 1080,
        int targetFps = 60,
        string? cursorOverlayPath = null)
    {
        var sb = new StringBuilder();
        var settings = manifest.Timeline?.Settings ?? new TimelineSettings();

        // 1. Dosya yollarını mutlak yola çözümle
        string videoPath = ResolveMediaPath(projectDir, manifest.VideoPath, "display-0.mp4");
        string micPath = ResolveMediaPath(projectDir, manifest.MicAudioPath ?? manifest.MicrophonePath, "microphone-0.wav");
        string sysPath = ResolveMediaPath(projectDir, manifest.SystemAudioPath, "system_audio-0.wav");

        // 2. Girdi dosyaları
        // Input 0: Ana Video
        sb.Append($"-y -i \"{videoPath}\" ");

        int nextInputIndex = 1;
        int cursorInputIndex = -1;

        // Input: Sanal İmleç Katmanı (.mov)
        bool hasCursorOverlay = !string.IsNullOrEmpty(cursorOverlayPath) && File.Exists(cursorOverlayPath) && new FileInfo(cursorOverlayPath).Length > 1000;
        if (hasCursorOverlay)
        {
            sb.Append($"-i \"{cursorOverlayPath}\" ");
            cursorInputIndex = nextInputIndex++;
        }

        // Input: Ses Dosyaları
        bool hasMic = !settings.MicMuted && !string.IsNullOrEmpty(micPath) && File.Exists(micPath) && new FileInfo(micPath).Length > 200;
        int micInputIndex = -1;
        if (hasMic)
        {
            sb.Append($"-i \"{micPath}\" ");
            micInputIndex = nextInputIndex++;
        }

        bool hasSys = !string.IsNullOrEmpty(sysPath) && File.Exists(sysPath) && new FileInfo(sysPath).Length > 200;
        int sysInputIndex = -1;
        if (hasSys)
        {
            sb.Append($"-i \"{sysPath}\" ");
            sysInputIndex = nextInputIndex++;
        }

        // 3. Video Filtre Zinciri (Filter Complex)
        var filterComplex = new StringBuilder();
        string currentVideoStream = "[0:v]";

        var videoClips = manifest.Timeline?.VideoTrack?.Clips;
        if (videoClips is { Count: > 0 } &&
            (videoClips.Count > 1 || videoClips[0].TrackOffset > 0.05 || videoClips[0].SourceStart > 0.05))
        {
            double totalDur = videoClips.Max(c => c.TrackOffset + Math.Max(0.01, c.SourceEnd - c.SourceStart));
            string totalDurStr = totalDur.ToString("F2", CultureInfo.InvariantCulture);

            filterComplex.Append($"color=c=black:s={targetWidth}x{targetHeight}:r={targetFps}:d={totalDurStr}[vbg]");

            string prevStream = "[vbg]";
            for (int i = 0; i < videoClips.Count; i++)
            {
                var c = videoClips[i];
                double dur = Math.Max(0.01, c.SourceEnd - c.SourceStart);
                double startOffset = c.TrackOffset;
                double endOffset = c.TrackOffset + dur;

                string sStart = c.SourceStart.ToString("F2", CultureInfo.InvariantCulture);
                string sEnd = c.SourceEnd.ToString("F2", CultureInfo.InvariantCulture);
                string tStart = startOffset.ToString("F2", CultureInfo.InvariantCulture);
                string tEnd = endOffset.ToString("F2", CultureInfo.InvariantCulture);

                filterComplex.Append($";[0:v]trim=start={sStart}:end={sEnd},setpts=PTS-STARTPTS,scale={targetWidth}:{targetHeight}[vclip{i}]");
                string nextStream = (i == videoClips.Count - 1) ? "[vcomp]" : $"[vcomp{i}]";
                filterComplex.Append($";{prevStream}[vclip{i}]overlay=0:0:enable='between(t,{tStart},{tEnd})'{nextStream}");
                prevStream = nextStream;
            }

            currentVideoStream = "[vcomp]";
        }

        // 3.1. Sanal İmleç Overlay (Zoom'dan önce uygulanır; böylece zoom yapıldığında imleç de zoomlanır)
        if (hasCursorOverlay && cursorInputIndex > 0)
        {
            if (filterComplex.Length > 0) filterComplex.Append(';');
            filterComplex.Append($"{currentVideoStream}[{cursorInputIndex}:v]overlay=0:0[vwithcursor]");
            currentVideoStream = "[vwithcursor]";
        }

        // 3.2. Zoom ve Pan Filtresi (ZoomEngineService kullanarak yumuşak geçişler)
        var zoomEffects = manifest.Timeline?.ZoomEffects ?? new List<ZoomEffect>();
        if (zoomEffects.Count > 0)
        {
            var zoomEngine = new ZoomEngineService();
            string zoompanFilter = zoomEngine.BuildZoompanFilter(zoomEffects, targetWidth, targetHeight, targetFps);
            if (filterComplex.Length > 0) filterComplex.Append(';');
            filterComplex.Append($"{currentVideoStream}{zoompanFilter}[vzoomed]");
            currentVideoStream = "[vzoomed]";
        }
        else
        {
            if (filterComplex.Length > 0) filterComplex.Append(';');
            filterComplex.Append($"{currentVideoStream}scale={targetWidth}:{targetHeight}[vscaled]");
            currentVideoStream = "[vscaled]";
        }

        // 3.3. Filigran (Watermark)
        if (settings.Watermark && !string.IsNullOrWhiteSpace(settings.WatermarkText))
        {
            string safeText = settings.WatermarkText.Replace("'", "\\'").Replace(":", "\\:");
            if (filterComplex.Length > 0) filterComplex.Append(';');
            filterComplex.Append($"{currentVideoStream}drawtext=text='{safeText}':x=w-tw-36:y=h-th-30:fontsize=26:fontcolor=white@0.75:box=1:boxcolor=black@0.35:boxborderw=6[vwatermark]");
            currentVideoStream = "[vwatermark]";
        }

        // 4. Ses Filtresi ve Miksaj
        string currentAudioStream = "[aout]";
        var audioFilters = new List<string>();

        // Ses Düzeyleri ve Geliştirmeleri
        double micVol = Math.Clamp((settings.MicVolume / 100.0) * (settings.VolumeEnhancement > 0 ? settings.VolumeEnhancement : 1.0), 0.0, 3.0);
        double sysVol = Math.Clamp(settings.SysVolume / 100.0, 0.0, 3.0);

        string micVolFilter = micVol != 1.0 ? $"volume={micVol.ToString("F2", CultureInfo.InvariantCulture)}" : "";
        string sysVolFilter = sysVol != 1.0 ? $"volume={sysVol.ToString("F2", CultureInfo.InvariantCulture)}" : "";

        if (settings.AudioNoiseReduction)
        {
            micVolFilter = string.IsNullOrEmpty(micVolFilter) ? "afftdn=nf=-25" : $"{micVolFilter},afftdn=nf=-25";
        }

        if (hasMic && hasSys)
        {
            string mFilter = string.IsNullOrEmpty(micVolFilter) ? $"[{micInputIndex}:a]anull[amic]" : $"[{micInputIndex}:a]{micVolFilter}[amic]";
            string sFilter = string.IsNullOrEmpty(sysVolFilter) ? $"[{sysInputIndex}:a]anull[asys]" : $"[{sysInputIndex}:a]{sysVolFilter}[asys]";
            if (filterComplex.Length > 0) filterComplex.Append(';');
            filterComplex.Append($"{mFilter};{sFilter};[amic][asys]amix=inputs=2:duration=longest[aout]");
        }
        else if (hasMic)
        {
            if (filterComplex.Length > 0) filterComplex.Append(';');
            if (!string.IsNullOrEmpty(micVolFilter))
                filterComplex.Append($"[{micInputIndex}:a]{micVolFilter}[aout]");
            else
                filterComplex.Append($"[{micInputIndex}:a]anull[aout]");
        }
        else if (hasSys)
        {
            if (filterComplex.Length > 0) filterComplex.Append(';');
            if (!string.IsNullOrEmpty(sysVolFilter))
                filterComplex.Append($"[{sysInputIndex}:a]{sysVolFilter}[aout]");
            else
                filterComplex.Append($"[{sysInputIndex}:a]anull[aout]");
        }
        else
        {
            // Ayrı ses yoksa ana videonun sesini kullan (varsa)
            currentAudioStream = "0:a?";
        }

        // 5. Parametreleri StringBuilder'a ekle
        sb.Append($"-filter_complex \"{filterComplex}\" -map \"{currentVideoStream}\" ");

        if (currentAudioStream == "[aout]")
        {
            sb.Append($"-map \"[aout]\" -c:a aac -b:a 192k ");
        }
        else
        {
            sb.Append($"-map 0:a? -c:a aac -b:a 192k ");
        }

        // 6. Video codec ve ilerleme izleme (pipe:1)
        sb.Append($"-c:v libx264 -preset faster -crf 18 -pix_fmt yuv420p -r {targetFps} -progress pipe:1 \"{outputPath}\"");

        return sb.ToString();
    }

    /// <summary>
    /// FFprobe veya FFmpeg kullanarak video dosyasının gerçek süresini (saniye cinsinden) kesin olarak okur.
    /// </summary>
    public static double GetVideoDuration(string videoPath)
    {
        try
        {
            if (!File.Exists(videoPath)) return 0;

            string ffmpegExe = FindFFmpeg();

            // 1. Doğrudan FFmpeg stream taraması ile gerçek süreyi bul
            var probePsi = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                Arguments = $"-i \"{videoPath}\" -f null -c copy -",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var probeProc = Process.Start(probePsi);
            if (probeProc != null)
            {
                string probeOut = probeProc.StandardError.ReadToEnd();
                probeProc.WaitForExit(4000);

                var matches = System.Text.RegularExpressions.Regex.Matches(probeOut, @"time=(\d+):(\d+):(\d+\.\d+)");
                if (matches.Count > 0)
                {
                    var match = matches[^1]; // Son time değerini al
                    int h = int.Parse(match.Groups[1].Value);
                    int m = int.Parse(match.Groups[2].Value);
                    double s = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                    double calculated = h * 3600 + m * 60 + s;
                    if (calculated > 0) return calculated;
                }

                int durIdx = probeOut.IndexOf("Duration: ", StringComparison.OrdinalIgnoreCase);
                if (durIdx != -1)
                {
                    string part = probeOut.Substring(durIdx + 10, 11);
                    if (TimeSpan.TryParse(part, out var ts) && ts.TotalSeconds > 0)
                    {
                        return ts.TotalSeconds;
                    }
                }
            }
        }
        catch { }

        return 0;
    }
}
