using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Helpers;

public static class FFmpegHelper
{
    private static string? _cachedFfmpegPath;

    public static string FindFFmpeg()
    {
        if (!string.IsNullOrEmpty(_cachedFfmpegPath) && File.Exists(_cachedFfmpegPath))
            return _cachedFfmpegPath;

        // Check common locations
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Packages\Gyan.FFmpeg.Essentials_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.1.1-essentials_build\bin\ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\ffmpeg\bin\ffmpeg.exe"),
            @"C:\ffmpeg\bin\ffmpeg.exe",
            "ffmpeg.exe" // on system PATH
        ];

        foreach (var p in candidates)
        {
            if (File.Exists(p))
            {
                _cachedFfmpegPath = p;
                return p;
            }
        }

        // Search in winget packages folder
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

    public static string BuildRenderCommand(
        ProjectManifest manifest,
        string outputPath,
        int targetWidth = 1920,
        int targetHeight = 1080,
        int targetFps = 60)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;

        // Inputs
        sb.Append($"-y -i \"{manifest.VideoPath}\" ");
        int audioIndex = 1;
        bool hasMic = !string.IsNullOrEmpty(manifest.MicAudioPath) && File.Exists(manifest.MicAudioPath);
        bool hasSys = !string.IsNullOrEmpty(manifest.SystemAudioPath) && File.Exists(manifest.SystemAudioPath);

        if (hasMic)
        {
            sb.Append($"-i \"{manifest.MicAudioPath}\" ");
            audioIndex++;
        }
        if (hasSys)
        {
            sb.Append($"-i \"{manifest.SystemAudioPath}\" ");
            audioIndex++;
        }

        // Build Video Filter for Zoom & Pan
        var filterComplex = new StringBuilder();
        var zoomEffects = manifest.Timeline.ZoomEffects;

        if (zoomEffects.Count > 0)
        {
            // Build zoom expression
            var zExpr = new StringBuilder("1");
            var xExpr = new StringBuilder($"(iw-iw/zoom)/2");
            var yExpr = new StringBuilder($"(ih-ih/zoom)/2");

            foreach (var z in zoomEffects)
            {
                double start = z.StartTime;
                double end = z.StartTime + z.Duration;
                double scale = Math.Max(1.0, z.Scale);

                // Center of zoom: calculate normalized or pixel offset
                double targetX = z.TargetX > 0 ? z.TargetX : targetWidth / 2.0;
                double targetY = z.TargetY > 0 ? z.TargetY : targetHeight / 2.0;

                string sStart = start.ToString("F2", inv);
                string sEnd = end.ToString("F2", inv);
                string sScale = scale.ToString("F2", inv);

                // z='if(between(t,1.2,3.2),1.5,prev)'
                zExpr.Insert(0, $"if(between(t\\,{sStart}\\,{sEnd})\\,{sScale}\\,");
                zExpr.Append(')');

                string sTargetX = targetX.ToString("F1", inv);
                string sTargetY = targetY.ToString("F1", inv);
                xExpr.Insert(0, $"if(between(t\\,{sStart}\\,{sEnd})\\,{sTargetX}-({targetWidth}/zoom)/2\\,");
                xExpr.Append(')');
                yExpr.Insert(0, $"if(between(t\\,{sStart}\\,{sEnd})\\,{sTargetY}-({targetHeight}/zoom)/2\\,");
                yExpr.Append(')');
            }

            filterComplex.Append($"[0:v]zoompan=z='{zExpr}':x='{xExpr}':y='{yExpr}':d=1:s={targetWidth}x{targetHeight}:fps={targetFps}[vfiltered]");
        }
        else
        {
            filterComplex.Append($"[0:v]scale={targetWidth}:{targetHeight}[vfiltered]");
        }

        // Audio mixing
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

        // Video codec & output
        sb.Append($"-c:v libx264 -preset faster -crf 18 -pix_fmt yuv420p -r {targetFps} -c:a aac -b:a 192k \"{outputPath}\"");

        return sb.ToString();
    }
}
