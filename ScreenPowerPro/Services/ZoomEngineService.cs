using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Services;

/// <summary>
/// Zoom ve pan animasyonlarının keyframe tabanlı hesaplandığı,
/// yumuşak geçişlerin (easeInOutCubic / smoothstep) uygulandığı ve FFmpeg filtre ifadelerinin
/// üretildiği merkezi zoom motoru servisi.
/// </summary>
public class ZoomEngineService
{
    // Varsayılan süre ve ölçek sabitleri
    public const double DefaultZoomDuration = 2.5;    // Varsayılan zoom kalma süresi (sn)
    public const double DefaultScale = 1.5;           // Varsayılan zoom yakınlaşma katsayısı
    public const double NearClickDistancePx = 550.0;  // Aynı bölge tıklama birleştirme mesafesi (piksel)
    public const double ZoomPreviewLeadSec = 0.35;    // Tıklamadan kaç saniye önce zoom başlasın (yumuşak giriş)
    public const double PostClickFollowSec = 2.2;     // Son tıklamadan sonra zoomun fareyi takip etme süresi (sn)
    public const double DefaultTransitionTime = 0.40; // Yumuşak yakınlaşma ve uzaklaşma süresi (sn)

    /// <summary>
    /// Keyframe veri yapısı: Belirli bir andaki zaman, ölçek ve hedef koordinatlar.
    /// </summary>
    public class Keyframe
    {
        public double Time { get; set; }
        public double Scale { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    /// <summary>
    /// Aktif zoom durumunu temsil eden model (canlı önizleme için).
    /// </summary>
    public class ActiveZoomState
    {
        public double Scale { get; set; } = 1.0;
        public double TargetX { get; set; }
        public double TargetY { get; set; }
    }

    /// <summary>
    /// Telemetri fare hareketleri listesinden belirtilen saniyedeki enterpole edilmiş koordinatı döner.
    /// İkili arama (binary search) ile O(log N) hızında çalışır.
    /// </summary>
    public static (double X, double Y)? GetInterpolatedCursorPosition(IReadOnlyList<MouseMoveEvent>? moves, double currentSec)
    {
        if (moves == null || moves.Count == 0) return null;

        if (currentSec <= moves[0].Timestamp)
            return (moves[0].X, moves[0].Y);

        if (currentSec >= moves[^1].Timestamp)
            return (moves[^1].X, moves[^1].Y);

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
            return (moves[^1].X, moves[^1].Y);

        var m1 = moves[idx];
        var m2 = moves[idx + 1];
        double dt = m2.Timestamp - m1.Timestamp;

        if (dt > 0.0001 && currentSec >= m1.Timestamp && currentSec <= m2.Timestamp)
        {
            double t = (currentSec - m1.Timestamp) / dt;
            double x = m1.X + (m2.X - m1.X) * t;
            double y = m1.Y + (m2.Y - m1.Y) * t;
            return (x, y);
        }

        return (m1.X, m1.Y);
    }

    /// <summary>
    /// Fare tıklama olaylarını analiz ederek akıllı, birleştirilmiş ve akıcı zoom efektleri listesi üretir.
    /// Üst üste aynı bölgede yapılan tıklamalarda zoom seviyesini bozmadan korur, süreyi uzatır.
    /// </summary>
    public List<ZoomEffect> GenerateZoomEffectsFromClicks(
        IEnumerable<MouseClickEvent> clicks,
        string autoZoomMode = "smooth",
        double defaultScale = DefaultScale,
        double maxVideoDurationSec = 0)
    {
        if (string.Equals(autoZoomMode, "none", StringComparison.OrdinalIgnoreCase))
        {
            return new List<ZoomEffect>();
        }

        var downClicks = clicks
            .Where(c => c.Type == "left_down" || c.Type == "right_down")
            .OrderBy(c => c.Timestamp)
            .ToList();

        if (downClicks.Count == 0) return new List<ZoomEffect>();

        var effects = new List<ZoomEffect>();
        int zoomIndex = 1;
        bool isInstant = string.Equals(autoZoomMode, "instant", StringComparison.OrdinalIgnoreCase);

        foreach (var click in downClicks)
        {
            if (effects.Count > 0)
            {
                var last = effects[^1];
                double lastEnd = last.StartTime + last.Duration;
                double dist = Math.Sqrt(Math.Pow(click.X - last.TargetX, 2) + Math.Pow(click.Y - last.TargetY, 2));

                bool isDuringOrNearLastZoom = click.Timestamp <= (lastEnd + 1.0);
                bool isSameRegion = dist <= NearClickDistancePx;

                if (isDuringOrNearLastZoom && isSameRegion)
                {
                    double newEnd = click.Timestamp + (isInstant ? 0.6 : PostClickFollowSec);
                    if (maxVideoDurationSec > 0) newEnd = Math.Min(newEnd, maxVideoDurationSec);

                    if (newEnd > lastEnd)
                    {
                        last.Duration = Math.Round(newEnd - last.StartTime, 3);
                    }

                    last.TargetX = Math.Round(last.TargetX * 0.35 + click.X * 0.65, 1);
                    last.TargetY = Math.Round(last.TargetY * 0.35 + click.Y * 0.65, 1);
                    continue;
                }
            }

            double leadTime = isInstant ? 0.08 : ZoomPreviewLeadSec;
            double startTimeSec = Math.Max(0, click.Timestamp - leadTime);
            double duration = isInstant ? 0.8 : (PostClickFollowSec + leadTime);

            if (maxVideoDurationSec > 0 && startTimeSec + duration > maxVideoDurationSec)
            {
                duration = Math.Max(0.5, maxVideoDurationSec - startTimeSec);
            }

            effects.Add(new ZoomEffect
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Name = $"Zoom {zoomIndex++}",
                StartTime = Math.Round(startTimeSec, 3),
                Duration = Math.Round(duration, 3),
                TargetX = Math.Round((double)click.X, 1),
                TargetY = Math.Round((double)click.Y, 1),
                Scale = defaultScale,
                Easing = isInstant ? "instant" : "ease-in-out"
            });
        }

        return effects;
    }

    /// <summary>
    /// Saniye cinsinden zamanı "MM:SS" veya "HH:MM:SS" formatında okunabilir metne dönüştürür.
    /// </summary>
    public static string FormatTimecode(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            return "00:00";
        }

        int totalSec = (int)Math.Floor(seconds);
        int h = totalSec / 3600;
        int m = (totalSec % 3600) / 60;
        int s = totalSec % 60;

        return h > 0
            ? $"{h:D2}:{m:D2}:{s:D2}"
            : $"{m:D2}:{s:D2}";
    }

    /// <summary>
    /// Geçerli bir video süresi doğrular, negatif veya tanımsız değerleri varsayılana eşitler.
    /// </summary>
    public static double NormalizeDuration(double value, double fallback = 0.0)
    {
        if (!double.IsNaN(value) && !double.IsInfinity(value) && value > 0)
        {
            return value;
        }
        if (!double.IsNaN(fallback) && !double.IsInfinity(fallback) && fallback > 0)
        {
            return fallback;
        }
        return 0.0;
    }

    /// <summary>
    /// Cubic Ease In/Out enterpolasyon eğrisi hesabı.
    /// </summary>
    public static double EaseInOutCubic(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t < 0.5
            ? 4.0 * t * t * t
            : 1.0 - Math.Pow(-2.0 * t + 2.0, 3.0) / 2.0;
    }

    /// <summary>
    /// Belirtilen video saniyesinde aktif bir zoom/pan durumu varsa koordinat ve ölçek değerini
    /// akıcı ease-in ve ease-out geçişleriyle hesaplar.
    /// Fare takibi ile fareyi merkezde tutar ve farenin her zaman zoom içinde kalmasını garanti eder.
    /// </summary>
    public ActiveZoomState? GetActiveZoomAtTime(
        IReadOnlyList<ZoomEffect>? effects,
        double timeSec,
        double defaultCenterX = 960,
        double defaultCenterY = 540,
        double cursorX = -1,
        double cursorY = -1,
        IReadOnlyList<MouseMoveEvent>? moves = null)
    {
        if (effects == null || effects.Count == 0) return null;

        if ((cursorX < 0 || cursorY < 0) && moves != null && moves.Count > 0)
        {
            var pt = GetInterpolatedCursorPosition(moves, timeSec);
            if (pt.HasValue)
            {
                cursorX = pt.Value.X;
                cursorY = pt.Value.Y;
            }
        }

        double vW = defaultCenterX * 2.0 > 0 ? defaultCenterX * 2.0 : 1920.0;
        double vH = defaultCenterY * 2.0 > 0 ? defaultCenterY * 2.0 : 1080.0;

        var sorted = effects.OrderBy(e => e.StartTime).ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            var e = sorted[i];
            double start = e.StartTime;
            double dur = Math.Max(0.5, e.Duration);
            double end = start + dur;

            bool hasNext = (i < sorted.Count - 1 && sorted[i + 1].StartTime <= end + 0.15);
            if (hasNext)
            {
                end = sorted[i + 1].StartTime;
                dur = Math.Max(0.3, end - start);
            }

            bool hasPrev = (i > 0 && start <= (sorted[i - 1].StartTime + sorted[i - 1].Duration + 0.15));

            if (timeSec >= start && timeSec <= end)
            {
                double targetScale = Math.Max(1.0, e.Scale);
                double viewW = vW / targetScale;
                double viewH = vH / targetScale;
                double halfW = viewW / 2.0;
                double halfH = viewH / 2.0;

                double tx = e.TargetX > 0 ? e.TargetX : defaultCenterX;
                double ty = e.TargetY > 0 ? e.TargetY : defaultCenterY;

                if (cursorX >= 0 && cursorY >= 0)
                {
                    double timeInEffect = timeSec - start;
                    double followFactor = Math.Clamp(timeInEffect / 0.25, 0.4, 1.0);
                    tx = tx * (1.0 - followFactor) + cursorX * followFactor;
                    ty = ty * (1.0 - followFactor) + cursorY * followFactor;
                }

                tx = Math.Clamp(tx, halfW, vW - halfW);
                ty = Math.Clamp(ty, halfH, vH - halfH);

                double transIn = hasPrev ? 0.30 : Math.Min(DefaultTransitionTime, dur * 0.35);
                double transOut = hasNext ? 0.30 : Math.Min(DefaultTransitionTime, dur * 0.35);

                double inEnd = start + transIn;
                double outStart = end - transOut;

                if (timeSec < inEnd && transIn > 0.001)
                {
                    double p = (timeSec - start) / transIn;
                    double ease = EaseInOutCubic(p);

                    double prevScale = hasPrev ? sorted[i - 1].Scale : 1.0;
                    double prevX = hasPrev ? Math.Clamp(sorted[i - 1].TargetX, halfW, vW - halfW) : defaultCenterX;
                    double prevY = hasPrev ? Math.Clamp(sorted[i - 1].TargetY, halfH, vH - halfH) : defaultCenterY;

                    double scale = prevScale + (targetScale - prevScale) * ease;
                    double cx = prevX + (tx - prevX) * ease;
                    double cy = prevY + (ty - prevY) * ease;

                    return new ActiveZoomState
                    {
                        Scale = Math.Round(scale, 4),
                        TargetX = Math.Round(cx, 2),
                        TargetY = Math.Round(cy, 2)
                    };
                }

                if (timeSec >= inEnd && timeSec <= outStart)
                {
                    return new ActiveZoomState
                    {
                        Scale = Math.Round(targetScale, 4),
                        TargetX = Math.Round(tx, 2),
                        TargetY = Math.Round(ty, 2)
                    };
                }

                if (timeSec > outStart && transOut > 0.001)
                {
                    double p = (timeSec - outStart) / transOut;
                    double ease = EaseInOutCubic(p);

                    if (hasNext)
                    {
                        var next = sorted[i + 1];
                        double nextViewW = vW / next.Scale;
                        double nextViewH = vH / next.Scale;
                        double nextTargetX = Math.Clamp(next.TargetX > 0 ? next.TargetX : defaultCenterX, nextViewW / 2.0, vW - nextViewW / 2.0);
                        double nextTargetY = Math.Clamp(next.TargetY > 0 ? next.TargetY : defaultCenterY, nextViewH / 2.0, vH - nextViewH / 2.0);

                        double scale = targetScale + (next.Scale - targetScale) * ease;
                        double cx = tx + (nextTargetX - tx) * ease;
                        double cy = ty + (nextTargetY - ty) * ease;

                        return new ActiveZoomState
                        {
                            Scale = Math.Round(scale, 4),
                            TargetX = Math.Round(cx, 2),
                            TargetY = Math.Round(cy, 2)
                        };
                    }
                    else
                    {
                        double scale = targetScale + (1.0 - targetScale) * ease;
                        double cx = tx + (defaultCenterX - tx) * ease;
                        double cy = ty + (defaultCenterY - ty) * ease;

                        return new ActiveZoomState
                        {
                            Scale = Math.Round(scale, 4),
                            TargetX = Math.Round(cx, 2),
                            TargetY = Math.Round(cy, 2)
                        };
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// FFmpeg zoompan filtre ifadesini akıcı, kesintisiz ve yumuşak geçişlerle oluşturur.
    /// Üst üste/ardışık zoomlarda zoom düzeyini korur ve hedefler arasında yumuşak pan uygular.
    /// </summary>
    public string BuildZoompanFilter(
        IReadOnlyList<ZoomEffect>? effects,
        int videoWidth = 1920,
        int videoHeight = 1080,
        int fps = 30)
    {
        if (effects == null || effects.Count == 0)
        {
            return $"scale={videoWidth}:{videoHeight}";
        }

        var sorted = effects.OrderBy(e => e.StartTime).ToList();

        string zExpr = "1";
        string xExpr = "iw/2-(iw/zoom/2)";
        string yExpr = "ih/2-(ih/zoom/2)";

        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            var e = sorted[i];
            double start = Math.Max(0, e.StartTime);
            double dur = Math.Max(0.5, e.Duration);
            double end = start + dur;

            bool hasNext = (i < sorted.Count - 1 && sorted[i + 1].StartTime <= end + 0.15);
            if (hasNext)
            {
                end = sorted[i + 1].StartTime;
                dur = Math.Max(0.3, end - start);
            }

            double transIn = Math.Min(DefaultTransitionTime, dur * 0.35);
            double transOut = Math.Min(DefaultTransitionTime, dur * 0.35);

            double tInEnd = start + transIn;
            double tOutStart = end - transOut;
            double scale = Math.Max(1.05, e.Scale);

            string sStart = start.ToString("F3", CultureInfo.InvariantCulture);
            string sEnd = end.ToString("F3", CultureInfo.InvariantCulture);
            string sInEnd = tInEnd.ToString("F3", CultureInfo.InvariantCulture);
            string sOutStart = tOutStart.ToString("F3", CultureInfo.InvariantCulture);

            string sTransIn = transIn.ToString("F3", CultureInfo.InvariantCulture);
            string sTransOut = transOut.ToString("F3", CultureInfo.InvariantCulture);
            string sScale = scale.ToString("F2", CultureInfo.InvariantCulture);
            string sScaleDelta = (scale - 1.0).ToString("F2", CultureInfo.InvariantCulture);

            string sTargetX = e.TargetX.ToString("F1", CultureInfo.InvariantCulture);
            string sTargetY = e.TargetY.ToString("F1", CultureInfo.InvariantCulture);

            string clampX = $"max(0,min(({sTargetX}-(iw/zoom/2)),iw-(iw/zoom)))";
            string clampY = $"max(0,min(({sTargetY}-(ih/zoom/2)),ih-(ih/zoom)))";

            string pIn = $"((in_time-{sStart})/{sTransIn})";
            string easeIn = $"({pIn}*{pIn}*(3-2*{pIn}))";
            string zIn = $"1+{sScaleDelta}*{easeIn}";

            string zOut;
            string curX;
            string curY;

            if (hasNext)
            {
                var next = sorted[i + 1];
                string sNextX = next.TargetX.ToString("F1", CultureInfo.InvariantCulture);
                string sNextY = next.TargetY.ToString("F1", CultureInfo.InvariantCulture);
                string clampNextX = $"max(0,min(({sNextX}-(iw/zoom/2)),iw-(iw/zoom)))";
                string clampNextY = $"max(0,min(({sNextY}-(ih/zoom/2)),ih-(ih/zoom)))";

                string pOut = $"((in_time-{sOutStart})/{sTransOut})";
                string easeOut = $"({pOut}*{pOut}*(3-2*{pOut}))";

                zOut = sScale; 
                curX = $"if(between(in_time,{sStart},{sInEnd}),(iw/2-(iw/zoom/2))+({clampX}-(iw/2-(iw/zoom/2)))*{easeIn},if(between(in_time,{sInEnd},{sOutStart}),{clampX},if(between(in_time,{sOutStart},{sEnd}),{clampX}+({clampNextX}-{clampX})*{easeOut},{xExpr})))";
                curY = $"if(between(in_time,{sStart},{sInEnd}),(ih/2-(ih/zoom/2))+({clampY}-(ih/2-(ih/zoom/2)))*{easeIn},if(between(in_time,{sInEnd},{sOutStart}),{clampY},if(between(in_time,{sOutStart},{sEnd}),{clampY}+({clampNextY}-{clampY})*{easeOut},{yExpr})))";
            }
            else
            {
                string pOut = $"(({sEnd}-in_time)/{sTransOut})";
                string easeOut = $"({pOut}*{pOut}*(3-2*{pOut}))";
                zOut = $"1+{sScaleDelta}*{easeOut}";

                curX = $"if(between(in_time,{sStart},{sInEnd}),(iw/2-(iw/zoom/2))+({clampX}-(iw/2-(iw/zoom/2)))*{easeIn},if(between(in_time,{sInEnd},{sOutStart}),{clampX},if(between(in_time,{sOutStart},{sEnd}),(iw/2-(iw/zoom/2))+({clampX}-(iw/2-(iw/zoom/2)))*{easeOut},{xExpr})))";
                curY = $"if(between(in_time,{sStart},{sInEnd}),(ih/2-(ih/zoom/2))+({clampY}-(ih/2-(ih/zoom/2)))*{easeIn},if(between(in_time,{sInEnd},{sOutStart}),{clampY},if(between(in_time,{sOutStart},{sEnd}),(ih/2-(ih/zoom/2))+({clampY}-(ih/2-(ih/zoom/2)))*{easeOut},{yExpr})))";
            }

            string curZ = $"if(between(in_time,{sStart},{sInEnd}),{zIn},if(between(in_time,{sInEnd},{sOutStart}),{sScale},if(between(in_time,{sOutStart},{sEnd}),{zOut},{zExpr})))";

            zExpr = curZ;
            xExpr = curX;
            yExpr = curY;
        }

        return $"zoompan=z='{zExpr}':x='{xExpr}':y='{yExpr}':d=1:s={videoWidth}x{videoHeight}:fps={fps}";
    }
}
