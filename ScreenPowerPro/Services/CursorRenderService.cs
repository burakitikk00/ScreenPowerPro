using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ScreenPowerPro.Helpers;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Services;

/// <summary>
/// Proje telemetri verilerini (fare hareketleri, tıklama olayları) ve manifest imleç ayarlarını
/// kullanarak saydam (RGBA / qtrle) bir video katmanı (overlay) üreten render servisi.
/// </summary>
public class CursorRenderService
{
    private readonly ProjectService _projectService;
    private Process? _currentProc;

    public CursorRenderService(ProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>
    /// Aktif render işlemini derhal iptal eder ve FFmpeg alt sürecini kapatır.
    /// </summary>
    public void Cancel()
    {
        try
        {
            if (_currentProc != null && !_currentProc.HasExited)
            {
                _currentProc.Kill();
            }
        }
        catch { }
    }

    /// <summary>
    /// Telemetri verilerinden sanal imleç ve tıklama animasyonlarını içeren saydam bir .mov katmanı oluşturur.
    /// İmleç kapalıysa veya telemetri yoksa null döner.
    /// </summary>
    public async Task<string?> RenderCursorOverlayAsync(
        string projectDir,
        ProjectManifest manifest,
        int targetWidth,
        int targetHeight,
        int targetFps,
        double totalDuration,
        CancellationToken cancellationToken = default,
        Action<double>? progressCallback = null)
    {
        var settings = manifest.Timeline?.Settings ?? new TimelineSettings();

        // İmleç gizliyse ve tıklama efekti yoksa render etmeye gerek yok
        bool cursorVisible = settings.CursorVisible;
        string clickEffect = settings.ClickEffect ?? "default";
        bool hasClickEffects = !string.Equals(clickEffect, "none", StringComparison.OrdinalIgnoreCase);

        if (!cursorVisible && !hasClickEffects)
        {
            return null;
        }

        var moves = _projectService.LoadMouseMoves(projectDir);
        var clicks = _projectService.LoadMouseClicks(projectDir);

        if ((moves == null || moves.Count == 0) && (clicks == null || clicks.Count == 0))
        {
            return null;
        }

        int srcWidth = manifest.Metadata?.Width > 0 ? manifest.Metadata.Width : 1920;
        int srcHeight = manifest.Metadata?.Height > 0 ? manifest.Metadata.Height : 1080;

        // İmleç katmanı için 30 FPS akıcı ve ultra hızlı render için idealdir
        int fps = Math.Min(30, targetFps > 0 ? targetFps : 30);
        int totalFrames = (int)Math.Ceiling(totalDuration * fps);
        if (totalFrames <= 0) totalFrames = 30;

        string tempFolder = Path.Combine(projectDir, "temp");
        if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);
        string outputMovPath = Path.Combine(tempFolder, $"cursor_overlay_{Guid.NewGuid():N}.mov");

        string ffmpegExe = FFmpegHelper.FindFFmpeg();
        string args = $"-y -f rawvideo -pix_fmt bgra -s {targetWidth}x{targetHeight} -r {fps} -i - -c:v qtrle -pix_fmt argb \"{outputMovPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegExe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var proc = new Process { StartInfo = psi };
        _currentProc = proc;

        // ÖNEMLİ: Windows pipe buffer deadlock'unu önlemek için standart hatayı (stderr) mutlaka tüketiyoruz!
        proc.ErrorDataReceived += (s, e) => { };

        try
        {
            proc.Start();
            proc.BeginErrorReadLine();

            using var reg = cancellationToken.Register(() =>
            {
                try
                {
                    if (!proc.HasExited) proc.Kill();
                }
                catch { }
            });

            var stdin = proc.StandardInput.BaseStream;

            int bytesPerFrame = targetWidth * targetHeight * 4;
            byte[] frameBuffer = new byte[bytesPerFrame];
            using var bmp = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            double cursorScaleFactor = (settings.CursorSize > 0 ? settings.CursorSize / 100.0 : 1.0) * (targetHeight / 1080.0);
            string cursorStyle = settings.CursorStyle ?? "default";
            bool hideIdle = settings.HideCursorWhenIdle;

            for (int f = 0; f < totalFrames; f++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double timeSec = f / (double)fps;

                // 1. Koordinat hesaplama (kaynak çözünürlükten hedef çözünürlüğe ölçekleme)
                PointF? cursorPos = GetInterpolatedCursorPosition(moves, timeSec);
                PointF? targetPos = null;
                if (cursorPos.HasValue)
                {
                    float normX = Math.Clamp(cursorPos.Value.X / srcWidth, 0f, 1f);
                    float normY = Math.Clamp(cursorPos.Value.Y / srcHeight, 0f, 1f);
                    targetPos = new PointF(normX * targetWidth, normY * targetHeight);
                }

                // 2. Boşta kalma (Idle) kontrolü
                float cursorOpacity = 1.0f;
                if (hideIdle && moves != null && moves.Count > 0 && cursorPos.HasValue)
                {
                    double lastMoveTime = GetLastMoveTimestamp(moves, timeSec);
                    double idleDiff = timeSec - lastMoveTime;
                    if (idleDiff > 1.5)
                    {
                        cursorOpacity = 0.0f;
                    }
                    else if (idleDiff > 1.2)
                    {
                        cursorOpacity = (float)Math.Clamp(1.0 - (idleDiff - 1.2) / 0.3, 0.0, 1.0);
                    }
                }

                // 3. Çizim işlemi
                g.Clear(Color.Transparent);

                // 3.1. Tıklama Efektlerini Çiz
                if (hasClickEffects && clicks != null && clicks.Count > 0)
                {
                    DrawClickEffects(g, clicks, timeSec, srcWidth, srcHeight, targetWidth, targetHeight, clickEffect, cursorScaleFactor);
                }

                // 3.2. Sanal İmleci Çiz
                if (cursorVisible && targetPos.HasValue && cursorOpacity > 0.01f)
                {
                    DrawCursorShape(g, targetPos.Value.X, targetPos.Value.Y, cursorStyle, (float)cursorScaleFactor, cursorOpacity);
                }

                // 4. Piksel verisini doğrudan FFmpeg stdin borusuna yaz
                var bmpData = bmp.LockBits(
                    new Rectangle(0, 0, targetWidth, targetHeight),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    Marshal.Copy(bmpData.Scan0, frameBuffer, 0, bytesPerFrame);
                }
                finally
                {
                    bmp.UnlockBits(bmpData);
                }

                await stdin.WriteAsync(frameBuffer, 0, bytesPerFrame, cancellationToken);

                if (f % 15 == 0 && totalFrames > 0)
                {
                    progressCallback?.Invoke((f / (double)totalFrames) * 100.0);
                }
            }

            stdin.Flush();
            stdin.Close();
            await proc.WaitForExitAsync(cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (File.Exists(outputMovPath) && new FileInfo(outputMovPath).Length > 1000)
            {
                return outputMovPath;
            }
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(); } catch { }
            try { if (File.Exists(outputMovPath)) File.Delete(outputMovPath); } catch { }
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CursorRenderService] Error: {ex.Message}");
            try { if (!proc.HasExited) proc.Kill(); } catch { }
            try { if (File.Exists(outputMovPath)) File.Delete(outputMovPath); } catch { }
            return null;
        }
        finally
        {
            _currentProc = null;
            proc.Dispose();
        }

        return null;
    }

    private static PointF? GetInterpolatedCursorPosition(IReadOnlyList<MouseMoveEvent>? moves, double currentSec)
    {
        if (moves == null || moves.Count == 0) return null;

        if (currentSec <= moves[0].Timestamp)
            return new PointF(moves[0].X, moves[0].Y);

        if (currentSec >= moves[^1].Timestamp)
            return new PointF(moves[^1].X, moves[^1].Y);

        int low = 0;
        int high = moves.Count - 1;
        int idx = 0;

        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (moves[mid].Timestamp <= currentSec)
            {
                idx = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (idx >= moves.Count - 1)
            return new PointF(moves[^1].X, moves[^1].Y);

        var m1 = moves[idx];
        var m2 = moves[idx + 1];
        double dt = m2.Timestamp - m1.Timestamp;

        if (dt > 0.0001 && currentSec >= m1.Timestamp && currentSec <= m2.Timestamp)
        {
            double t = (currentSec - m1.Timestamp) / dt;
            float x = (float)(m1.X + (m2.X - m1.X) * t);
            float y = (float)(m1.Y + (m2.Y - m1.Y) * t);
            return new PointF(x, y);
        }

        return new PointF(m1.X, m1.Y);
    }

    private static double GetLastMoveTimestamp(IReadOnlyList<MouseMoveEvent> moves, double currentSec)
    {
        int low = 0, high = moves.Count - 1, found = 0;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (moves[mid].Timestamp <= currentSec)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        return moves[found].Timestamp;
    }

    private static void DrawCursorShape(Graphics g, float x, float y, string style, float scale, float opacity)
    {
        int alpha = Math.Clamp((int)(255 * opacity), 0, 255);

        switch (style)
        {
            case "arrow_black":
                DrawStandardArrow(g, x, y, scale, Color.FromArgb(alpha, 30, 31, 39), Color.FromArgb(alpha, 255, 255, 255));
                break;

            case "pointer_hand":
                DrawPointerHand(g, x, y, scale, alpha);
                break;

            case "crosshair":
                DrawCrosshair(g, x, y, scale, alpha);
                break;

            case "circle_dot":
                DrawCircleDot(g, x, y, scale, alpha);
                break;

            case "ibeam":
                DrawIBeam(g, x, y, scale, alpha);
                break;

            case "spotlight":
                float spotRadius = 32f * scale;
                using (var spotBrush = new SolidBrush(Color.FromArgb((int)(alpha * 0.25f), 192, 193, 255)))
                {
                    g.FillEllipse(spotBrush, x - spotRadius, y - spotRadius, spotRadius * 2, spotRadius * 2);
                }
                using (var spotPen = new Pen(Color.FromArgb((int)(alpha * 0.65f), 192, 193, 255), 1.5f * scale))
                {
                    g.DrawEllipse(spotPen, x - spotRadius, y - spotRadius, spotRadius * 2, spotRadius * 2);
                }
                DrawStandardArrow(g, x, y, scale, Color.FromArgb(alpha, 255, 255, 255), Color.FromArgb(alpha, 30, 31, 39));
                break;

            case "highlight_yellow":
                float hlRadius = 24f * scale;
                using (var hlBrush = new SolidBrush(Color.FromArgb((int)(alpha * 0.45f), 255, 230, 0)))
                {
                    g.FillEllipse(hlBrush, x - hlRadius, y - hlRadius, hlRadius * 2, hlRadius * 2);
                }
                using (var hlPen = new Pen(Color.FromArgb((int)(alpha * 0.8f), 255, 230, 0), 1.5f * scale))
                {
                    g.DrawEllipse(hlPen, x - hlRadius, y - hlRadius, hlRadius * 2, hlRadius * 2);
                }
                DrawStandardArrow(g, x, y, scale, Color.FromArgb(alpha, 255, 255, 255), Color.FromArgb(alpha, 30, 31, 39));
                break;

            case "arrow_cyan":
                DrawStandardArrow(g, x, y, scale, Color.FromArgb(alpha, 0, 229, 255), Color.FromArgb(alpha, 15, 20, 35));
                break;

            case "arrow_purple":
                DrawStandardArrow(g, x, y, scale, Color.FromArgb(alpha, 192, 193, 255), Color.FromArgb(alpha, 25, 15, 45));
                break;

            case "default":
            default:
                DrawStandardArrow(g, x, y, scale, Color.FromArgb(alpha, 255, 255, 255), Color.FromArgb(alpha, 30, 31, 39));
                break;
        }
    }

    private static void DrawStandardArrow(Graphics g, float x, float y, float scale, Color fill, Color outline)
    {
        PointF[] pts =
        {
            new PointF(x, y),
            new PointF(x, y + 20f * scale),
            new PointF(x + 5.5f * scale, y + 14.5f * scale),
            new PointF(x + 9.5f * scale, y + 23.5f * scale),
            new PointF(x + 13f * scale, y + 22f * scale),
            new PointF(x + 8.5f * scale, y + 13.5f * scale),
            new PointF(x + 15f * scale, y + 13.5f * scale)
        };

        using (var shadowBrush = new SolidBrush(Color.FromArgb(Math.Min((int)fill.A, 60), 0, 0, 0)))
        {
            PointF[] shadowPts = pts.Select(p => new PointF(p.X + 1.5f * scale, p.Y + 2f * scale)).ToArray();
            g.FillPolygon(shadowBrush, shadowPts);
        }

        using (var brush = new SolidBrush(fill))
        {
            g.FillPolygon(brush, pts);
        }

        using (var pen = new Pen(outline, Math.Max(1.2f, 1.6f * scale)))
        {
            pen.LineJoin = LineJoin.Round;
            g.DrawPolygon(pen, pts);
        }
    }

    private static void DrawCrosshair(Graphics g, float x, float y, float scale, int alpha)
    {
        float len = 12f * scale;
        float gap = 3.5f * scale;
        using var pen = new Pen(Color.FromArgb(alpha, 0, 240, 255), Math.Max(1.5f, 2f * scale));
        g.DrawLine(pen, x - len, y, x - gap, y);
        g.DrawLine(pen, x + gap, y, x + len, y);
        g.DrawLine(pen, x, y - len, x, y - gap);
        g.DrawLine(pen, x, y + gap, x, y + len);
    }

    private static void DrawCircleDot(Graphics g, float x, float y, float scale, int alpha)
    {
        float r = 9f * scale;
        using (var brush = new SolidBrush(Color.FromArgb((int)(alpha * 0.45f), 255, 255, 255)))
        {
            g.FillEllipse(brush, x - r, y - r, r * 2, r * 2);
        }
        using (var pen = new Pen(Color.FromArgb(alpha, 255, 255, 255), Math.Max(1.5f, 2f * scale)))
        {
            g.DrawEllipse(pen, x - r, y - r, r * 2, r * 2);
        }
    }

    private static void DrawIBeam(Graphics g, float x, float y, float scale, int alpha)
    {
        float h = 16f * scale;
        float w = 6f * scale;
        using var pen = new Pen(Color.FromArgb(alpha, 255, 255, 255), Math.Max(1.2f, 1.8f * scale));
        g.DrawLine(pen, x, y - h / 2, x, y + h / 2);
        g.DrawLine(pen, x - w / 2, y - h / 2, x + w / 2, y - h / 2);
        g.DrawLine(pen, x - w / 2, y + h / 2, x + w / 2, y + h / 2);
    }

    private static void DrawPointerHand(Graphics g, float x, float y, float scale, int alpha)
    {
        PointF[] finger =
        {
            new PointF(x, y),
            new PointF(x + 4f * scale, y + 2f * scale),
            new PointF(x + 4f * scale, y + 10f * scale),
            new PointF(x + 10f * scale, y + 12f * scale),
            new PointF(x + 10f * scale, y + 18f * scale),
            new PointF(x + 3f * scale, y + 20f * scale),
            new PointF(x - 2f * scale, y + 15f * scale),
            new PointF(x - 2f * scale, y + 8f * scale)
        };
        using (var brush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
        {
            g.FillPolygon(brush, finger);
        }
        using (var pen = new Pen(Color.FromArgb(alpha, 30, 31, 39), Math.Max(1.2f, 1.5f * scale)))
        {
            pen.LineJoin = LineJoin.Round;
            g.DrawPolygon(pen, finger);
        }
    }

    private static void DrawClickEffects(
        Graphics g,
        IReadOnlyList<MouseClickEvent> clicks,
        double timeSec,
        int srcW,
        int srcH,
        int targetW,
        int targetH,
        string effect,
        double scaleFactor)
    {
        const double effectDuration = 0.38;

        var activeClicks = clicks.Where(c =>
            (c.Type == "left_down" || c.Type == "right_down") &&
            timeSec >= c.Timestamp &&
            timeSec <= c.Timestamp + effectDuration);

        foreach (var c in activeClicks)
        {
            float normX = Math.Clamp(c.X / (float)srcW, 0f, 1f);
            float normY = Math.Clamp(c.Y / (float)srcH, 0f, 1f);
            float cx = normX * targetW;
            float cy = normY * targetH;

            float elapsed = (float)(timeSec - c.Timestamp);
            float progress = Math.Clamp(elapsed / (float)effectDuration, 0f, 1f);
            float alphaProgress = 1.0f - progress;
            int alpha = (int)(255 * alphaProgress);

            float baseScale = (float)scaleFactor;

            if (effect == "sparkle" || effect == "firework" || effect == "christmas")
            {
                DrawParticleBurst(g, cx, cy, effect, progress, alpha, baseScale);
            }
            else
            {
                DrawExpandingRing(g, cx, cy, effect, progress, alpha, baseScale);
            }
        }
    }

    private static void DrawExpandingRing(Graphics g, float cx, float cy, string effect, float progress, int alpha, float scale)
    {
        float radius;
        Color strokeColor;
        Color fillColor;
        float strokeWidth = 2f * scale;

        switch (effect)
        {
            case "ripple":
                radius = (10f + progress * 32f) * scale;
                strokeColor = Color.FromArgb(alpha, 192, 193, 255);
                fillColor = Color.FromArgb((int)(alpha * 0.25f), 192, 193, 255);
                break;

            case "ring":
                radius = (12f + progress * 28f) * scale;
                strokeColor = Color.FromArgb(alpha, 0, 229, 255);
                fillColor = Color.Transparent;
                strokeWidth = 2.5f * scale;
                break;

            case "diffusion":
                radius = (8f + progress * 36f) * scale;
                strokeColor = Color.FromArgb(alpha, 255, 230, 0);
                fillColor = Color.FromArgb((int)(alpha * 0.3f), 255, 230, 0);
                strokeWidth = 1.8f * scale;
                break;

            case "spotlight":
                radius = (10f + progress * 24f) * scale;
                strokeColor = Color.FromArgb(alpha, 255, 255, 255);
                fillColor = Color.FromArgb((int)(alpha * 0.4f), 255, 255, 255);
                break;

            case "default":
            default:
                radius = (10f + progress * 22f) * scale;
                strokeColor = Color.FromArgb(alpha, 192, 193, 255);
                fillColor = Color.FromArgb((int)(alpha * 0.28f), 192, 193, 255);
                break;
        }

        if (fillColor != Color.Transparent && radius > 1)
        {
            using var fillBrush = new SolidBrush(fillColor);
            g.FillEllipse(fillBrush, cx - radius, cy - radius, radius * 2, radius * 2);
        }

        if (strokeColor.A > 0 && radius > 1)
        {
            using var pen = new Pen(strokeColor, strokeWidth);
            g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
        }
    }

    private static void DrawParticleBurst(Graphics g, float cx, float cy, string effect, float progress, int alpha, float scale)
    {
        int count = effect == "firework" ? 8 : 6;
        Color[] colors = effect switch
        {
            "christmas" => new[] { Color.FromArgb(alpha, 239, 68, 68), Color.FromArgb(alpha, 34, 197, 94), Color.FromArgb(alpha, 255, 215, 0) },
            "firework" => new[] { Color.FromArgb(alpha, 244, 63, 94), Color.FromArgb(alpha, 59, 130, 246), Color.FromArgb(alpha, 234, 179, 8), Color.FromArgb(alpha, 168, 85, 247) },
            _ => new[] { Color.FromArgb(alpha, 250, 204, 21), Color.FromArgb(alpha, 255, 255, 255), Color.FromArgb(alpha, 192, 193, 255) }
        };

        float dist = (12f + progress * 34f) * scale;
        float particleRadius = Math.Max(1.5f, 4f * (1.0f - progress) * scale);

        for (int i = 0; i < count; i++)
        {
            double angle = (2 * Math.PI / count) * i;
            float px = cx + (float)(Math.Cos(angle) * dist);
            float py = cy + (float)(Math.Sin(angle) * dist);

            var color = colors[i % colors.Length];
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, px - particleRadius, py - particleRadius, particleRadius * 2, particleRadius * 2);
        }
    }
}
