using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ScreenPowerPro.Core.Tracking;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Services;

/// <summary>
/// Zoom ve pan animasyonlarının keyframe tabanlı hesaplandığı,
/// doğrusal olmayan yumuşak geçişlerin (EaseOutCubic / EaseOutSine) uygulandığı,
/// akıllı durum makinesi (üst üste zoom engelleme ve sanal sınır ihlali kontrolü) ve
/// FFmpeg filtre ifadelerinin üretildiği merkezi zoom motoru servisi.
/// </summary>
public class ZoomEngineService
{
    // Dinamik ayarlar için SettingsManager kullanımı:
    public static double DefaultScale => SettingsManager.Instance.MaxZoomRatio;           // Varsayılan zoom yakınlaşma katsayısı
    public const double NearClickDistancePx = 550.0;  // Aynı bölge tıklama birleştirme mesafesi (piksel)
    public static double ZoomPreviewLeadSec => SettingsManager.Instance.PreClickAnticipationMs / 1000.0;    // Tıklamadan kaç saniye önce zoom başlasın (yumuşak giriş)
    public static double PostClickFollowSec => SettingsManager.Instance.ZoomDuration;     // Son tıklamadan sonra zoomun ekranda kalma süresi (sn)
    public static double DefaultTransitionTime => SettingsManager.Instance.ZoomSpeed; // Yumuşak yakınlaşma ve uzaklaşma süresi (sn)
    public static bool CancelOnOutOfBounds => SettingsManager.Instance.CancelOnOutOfBounds; // Sınır ihlalinde zoom iptali


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
    /// MouseFrameData telemetrisi için MouseInterpolator kullanarak enterpole koordinatı döner.
    /// </summary>
    public static (double X, double Y)? GetInterpolatedCursorPosition(IReadOnlyList<MouseFrameData>? frames, double currentSec)
    {
        if (frames == null || frames.Count == 0) return null;
        var interpolator = new MouseInterpolator(frames);
        var pt = interpolator.GetInterpolatedPosition(currentSec * 1000.0);
        return (pt.X, pt.Y);
    }

    /// <summary>
    /// Telemetri fare hareketleri listesinden belirtilen saniyedeki enterpole edilmiş koordinatı döner.
    /// İki milisaniye kaydı arasında Linear Interpolation (Lerp) uygular.
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

        if (dt > 0.00001 && currentSec >= m1.Timestamp && currentSec <= m2.Timestamp)
        {
            double t = Math.Clamp((currentSec - m1.Timestamp) / dt, 0.0, 1.0);
            double x = m1.X + (m2.X - m1.X) * t;
            double y = m1.Y + (m2.Y - m1.Y) * t;
            return (x, y);
        }

        return (m1.X, m1.Y);
    }

    /// <summary>
    /// Fare tıklama olaylarını analiz ederek akıllı, birleştirilmiş ve akıcı zoom efektleri listesi üretir.
    /// KURAL: Üst üste aynı bölgede yapılan tıklamalarda zoom seviyesini (Scale) artırmaz,
    /// sadece ekranda kalma süresini uzatır.
    /// </summary>
    public List<ZoomEffect> GenerateZoomEffectsFromClicks(
        IEnumerable<MouseClickEvent> clicks,
        IEnumerable<MouseMoveEvent>? moves = null,
        string autoZoomMode = "smooth",
        double? defaultScale = null,
        double maxVideoDurationSec = 0,
        double defaultCenterX = 960,
        double defaultCenterY = 540)
    {
        double actualScale = defaultScale ?? DefaultScale;
        if (string.Equals(autoZoomMode, "none", StringComparison.OrdinalIgnoreCase))
        {
            return new List<ZoomEffect>();
        }

        var allClicks = clicks.OrderBy(c => c.Timestamp).ToList();
        var downClicks = allClicks.Where(c => c.Type == "left_down" || c.Type == "right_down").ToList();
        if (downClicks.Count == 0) return new List<ZoomEffect>();

        var allMoves = moves?.OrderBy(m => m.Timestamp).ToList() ?? new List<MouseMoveEvent>();

        var effects = new List<ZoomEffect>();
        int zoomIndex = 1;

        double vW = defaultCenterX * 2.0 > 0 ? defaultCenterX * 2.0 : 1920.0;
        double vH = defaultCenterY * 2.0 > 0 ? defaultCenterY * 2.0 : 1080.0;
        double viewW = vW / actualScale;
        double viewH = vH / actualScale;
        double halfW = viewW / 2.0;
        double halfH = viewH / 2.0;

        // Settings Manager'dan değerleri al
        double idleTimeout = PostClickFollowSec;
        double boundaryDeadzone = 300.0; // 300px sanal güvenli bölge
        double velocityMaxDist = 200.0;
        double velocityTimeWindow = 0.05; // 50ms

        bool isZoomed = false;
        double currentZoomStartTime = 0;
        double lastActivityTime = 0;
        double currentTargetX = defaultCenterX;
        double currentTargetY = defaultCenterY;
        
        ZoomEffect? currentEffect = null;

        // Tıklama ve hareketleri kronolojik sıraya diz
        var events = new List<(double Time, bool IsClick, double X, double Y, MouseClickEvent? Click, MouseMoveEvent? Move)>();
        foreach (var c in downClicks) events.Add((c.Timestamp, true, c.X, c.Y, c, null));
        foreach (var m in allMoves) events.Add((m.Timestamp, false, m.X, m.Y, null, m));
        
        events = events.OrderBy(e => e.Time).ToList();

        double lastMoveX = -1;
        double lastMoveY = -1;
        double lastMoveTime = -1;

        Action<double> FinalizeCurrentZoom = (double endTime) => {
            if (currentEffect != null)
            {
                double dur = endTime - currentEffect.StartTime;
                if (dur < 0.5) dur = 0.5; // Min süre
                currentEffect.Duration = Math.Round(dur, 3);
                effects.Add(currentEffect);
                currentEffect = null;
            }
            isZoomed = false;
        };

        foreach (var ev in events)
        {
            double time = ev.Time;
            double x = ev.X;
            double y = ev.Y;

            if (isZoomed)
            {
                // 1. Idle Timeout (Zaman Aşımı) Kontrolü
                if ((time - lastActivityTime) > idleTimeout)
                {
                    FinalizeCurrentZoom(lastActivityTime + idleTimeout);
                }
                
                // 2. Velocity Check (İvme/Hız Kontrolü)
                else if (!ev.IsClick && lastMoveTime > 0 && (time - lastMoveTime) <= velocityTimeWindow * 2)
                {
                    double dist = Math.Sqrt(Math.Pow(x - lastMoveX, 2) + Math.Pow(y - lastMoveY, 2));
                    double speed = dist / (time - lastMoveTime); // px / sec
                    if (speed > (velocityMaxDist / velocityTimeWindow)) // Hızlı hareket (örn. 4000 px/sec)
                    {
                        FinalizeCurrentZoom(time);
                    }
                }
                
                // 3. Boundary Exit (Sınır Çıkışı)
                else if (!ev.IsClick && CancelOnOutOfBounds)
                {
                    double distFromCenter = Math.Sqrt(Math.Pow(x - currentTargetX, 2) + Math.Pow(y - currentTargetY, 2));
                    if (distFromCenter > boundaryDeadzone)
                    {
                        FinalizeCurrentZoom(time);
                    }
                }
            }

            if (ev.IsClick)
            {
                lastActivityTime = time;

                if (!isZoomed)
                {
                    // Yeni bir zoom oturumu başlat - Tıklamanın yapıldığı orijinal koordinatları sakla
                    isZoomed = true;
                    currentZoomStartTime = Math.Max(0, time - (autoZoomMode == "instant" ? 0.08 : ZoomPreviewLeadSec));
                    currentTargetX = x;
                    currentTargetY = y;

                    currentEffect = new ZoomEffect
                    {
                        Id = Guid.NewGuid().ToString("N")[..8],
                        Name = $"Zoom {zoomIndex++}",
                        StartTime = Math.Round(currentZoomStartTime, 3),
                        Scale = actualScale,
                        TargetX = Math.Round(x, 1),
                        TargetY = Math.Round(y, 1),
                        Easing = autoZoomMode == "instant" ? "instant" : SettingsManager.Instance.ZoomEasingFunction.ToLowerInvariant()
                    };
                }
                else
                {
                    // Zaten zoom durumundayız. BÜYÜTME (Scale artmaz). Sadece Panning yap.
                    // Mevcut zoom'u burada sonlandırıp ardışık yeni bir ZoomEffect başlatarak
                    // Render katmanının smooth pan yapmasını sağlıyoruz.
                    double transitionStart = time;
                    FinalizeCurrentZoom(transitionStart);
                    
                    isZoomed = true;
                    currentTargetX = x;
                    currentTargetY = y;
                    
                    currentEffect = new ZoomEffect
                    {
                        Id = Guid.NewGuid().ToString("N")[..8],
                        Name = $"Zoom {zoomIndex++}",
                        StartTime = Math.Round(transitionStart, 3),
                        Scale = actualScale,
                        TargetX = Math.Round(x, 1),
                        TargetY = Math.Round(y, 1),
                        Easing = autoZoomMode == "instant" ? "instant" : SettingsManager.Instance.ZoomEasingFunction.ToLowerInvariant()
                    };
                }
            }
            else
            {
                // Hareketi anlamlıysa activity zamanını güncelle (örn > 10px hareket)
                if (lastMoveTime > 0)
                {
                    double dist = Math.Sqrt(Math.Pow(x - lastMoveX, 2) + Math.Pow(y - lastMoveY, 2));
                    if (dist > 10)
                    {
                        lastActivityTime = time;
                    }
                }
                
                lastMoveX = x;
                lastMoveY = y;
                lastMoveTime = time;
            }
        }

        if (isZoomed)
        {
            FinalizeCurrentZoom(lastActivityTime + idleTimeout);
        }

        if (maxVideoDurationSec > 0)
        {
            foreach (var e in effects)
            {
                if (e.StartTime + e.Duration > maxVideoDurationSec)
                {
                    e.Duration = Math.Max(0.5, maxVideoDurationSec - e.StartTime);
                }
            }
            effects.RemoveAll(e => e.StartTime >= maxVideoDurationSec);
        }

        return effects;
    }

    /// <summary>
    /// MouseFrameData telemetrisi için akıllı zoom efektleri listesi üretir.
    /// </summary>
    public List<ZoomEffect> GenerateZoomEffectsFromFrames(
        IEnumerable<MouseFrameData> frames,
        string autoZoomMode = "smooth",
        double? defaultScale = null,
        double maxVideoDurationSec = 0)
    {

        var clickEvents = frames
            .Where(f => f.EventType == MouseEventType.LeftDown || f.EventType == MouseEventType.RightDown)
            .Select(f => MouseClickEvent.FromFrameData(f));

        var moveEvents = frames
            .Where(f => f.EventType == MouseEventType.Move)
            .Select(f => MouseMoveEvent.FromFrameData(f));

        return GenerateZoomEffectsFromClicks(clickEvents, moveEvents, autoZoomMode, defaultScale, maxVideoDurationSec);
    }

    /// <summary>
    /// Akıllı zoom durum makinesi örneği oluşturur.
    /// </summary>
    public static SmartZoomStateMachine CreateSmartZoomStateMachine(
        float? targetScale = null,
        double? zoomInMs = null,
        double? zoomOutMs = null,
        double? holdMs = null,
        float boundaryW = 320f,
        float boundaryH = 240f)
    {
        var settings = SettingsManager.Instance;
        double speedMs = settings.ZoomSpeed * 1000.0;
        return new SmartZoomStateMachine
        {
            TargetScale = targetScale ?? (float)settings.MaxZoomRatio,
            ZoomInDurationMs = zoomInMs ?? Math.Max(100.0, speedMs * 0.7),
            ZoomOutDurationMs = zoomOutMs ?? Math.Max(100.0, speedMs * 0.85),
            DefaultHoldDurationMs = holdMs ?? (settings.ZoomDuration * 1000.0),
            VirtualBoundarySize = new System.Drawing.SizeF(boundaryW, boundaryH)
        };
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
    /// Cubic-Ease-Out: 1 - (1 - t)^3
    /// Tıklama anında ekranın ani değil, doğrusal olmayan pürüzsüz bir ivmeyle büyümesini sağlar.
    /// </summary>
    public static double EaseOutCubic(double t)
    {
        double f = 1.0 - Math.Clamp(t, 0.0, 1.0);
        return 1.0 - (f * f * f);
    }

    /// <summary>
    /// Sine-Ease-Out: sin(t * PI / 2)
    /// </summary>
    public static double EaseOutSine(double t)
    {
        return Math.Sin(Math.Clamp(t, 0.0, 1.0) * (Math.PI / 2.0));
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

    public static double EaseOutQuad(double t)
    {
        double p = Math.Clamp(t, 0.0, 1.0);
        return p * (2.0 - p);
    }

    public static double EaseOutQuart(double t)
    {
        double f = 1.0 - Math.Clamp(t, 0.0, 1.0);
        return 1.0 - (f * f * f * f);
    }

    public static (double x1, double y1, double x2, double y2) ParseCubicBezier(string? easing)
    {
        if (string.IsNullOrWhiteSpace(easing))
            return (0.215, 0.61, 0.355, 1.0); // default cubic-out

        string clean = easing.Trim().ToLowerInvariant().Replace(" ", "");
        if (clean.StartsWith("cubic-bezier(") && clean.EndsWith(")"))
        {
            var parts = clean.Substring(13, clean.Length - 14).Split(',');
            if (parts.Length == 4 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x1) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y1) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double x2) &&
                double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double y2))
            {
                return (Math.Clamp(x1, 0.0, 1.0), y1, Math.Clamp(x2, 0.0, 1.0), y2);
            }
        }
        else if (clean == "linear") return (0.0, 0.0, 1.0, 1.0);
        else if (clean == "quad-out" || clean == "quadout") return (0.25, 0.46, 0.45, 0.94);
        else if (clean == "cubic-out" || clean == "cubicout") return (0.215, 0.61, 0.355, 1.0);
        else if (clean == "quartic-out" || clean == "quarticout") return (0.165, 0.84, 0.44, 1.0);
        else if (clean == "ease-in-out" || clean == "easeinout") return (0.42, 0.0, 0.58, 1.0);

        return (0.215, 0.61, 0.355, 1.0);
    }

    public static double SolveCubicBezier(double p, double x1, double y1, double x2, double y2)
    {
        p = Math.Clamp(p, 0.0, 1.0);
        if (p <= 0.0) return 0.0;
        if (p >= 1.0) return 1.0;

        double cx = 3.0 * x1;
        double bx = 3.0 * (x2 - x1) - cx;
        double ax = 1.0 - cx - bx;

        double cy = 3.0 * y1;
        double by = 3.0 * (y2 - y1) - cy;
        double ay = 1.0 - cy - by;

        double SampleCurveX(double t) => ((ax * t + bx) * t + cx) * t;
        double SampleCurveY(double t) => ((ay * t + by) * t + cy) * t;
        double SampleCurveDerivativeX(double t) => (3.0 * ax * t + 2.0 * bx) * t + cx;

        double t2 = p;
        for (int i = 0; i < 8; i++)
        {
            double x2Val = SampleCurveX(t2) - p;
            if (Math.Abs(x2Val) < 1e-6)
                return SampleCurveY(t2);

            double d2 = SampleCurveDerivativeX(t2);
            if (Math.Abs(d2) < 1e-6)
                break;

            t2 -= x2Val / d2;
            t2 = Math.Clamp(t2, 0.0, 1.0);
        }

        double t0 = 0.0, t1 = 1.0;
        t2 = p;
        while (t0 < t1)
        {
            double x2Val = SampleCurveX(t2);
            if (Math.Abs(x2Val - p) < 1e-5)
                return SampleCurveY(t2);
            if (p > x2Val)
                t0 = t2;
            else
                t1 = t2;
            t2 = (t1 + t0) * 0.5;
            if (Math.Abs(t1 - t0) < 1e-5) break;
        }

        return SampleCurveY(t2);
    }

    public static double ApplyEasing(double p, string? easing)
    {
        if (string.IsNullOrWhiteSpace(easing))
            return EaseOutCubic(p);

        string key = easing.Trim().ToLowerInvariant().Replace(" ", "-");
        if (key.StartsWith("cubic-bezier"))
        {
            var (x1, y1, x2, y2) = ParseCubicBezier(easing);
            return SolveCubicBezier(p, x1, y1, x2, y2);
        }

        return key switch
        {
            "linear" => p,
            "quad-out" or "quadout" or "ease-out-quad" => EaseOutQuad(p),
            "cubic-out" or "cubicout" or "ease-out-cubic" => EaseOutCubic(p),
            "quartic-out" or "quarticout" or "ease-out-quart" => EaseOutQuart(p),
            "ease-in-out" or "easeinout" or "ease-in-out-cubic" => EaseInOutCubic(p),
            "sine-out" or "ease-out-sine" => EaseOutSine(p),
            _ => EaseOutCubic(p)
        };
    }

    public static string GetFfmpegEaseExpression(string pVar, string? easing, bool isEaseIn = true)
    {
        if (string.IsNullOrWhiteSpace(easing))
            return isEaseIn ? $"(1-pow(1-{pVar},3))" : $"({pVar}*(2-{pVar}))";

        string key = easing.Trim().ToLowerInvariant().Replace(" ", "-");
        if (key.StartsWith("cubic-bezier"))
        {
            var (x1, y1, x2, y2) = ParseCubicBezier(easing);
            string sy1 = y1.ToString("F3", CultureInfo.InvariantCulture);
            string sy2 = y2.ToString("F3", CultureInfo.InvariantCulture);
            return $"(3*pow(1-{pVar},2)*{pVar}*{sy1}+3*(1-{pVar})*pow({pVar},2)*{sy2}+pow({pVar},3))";
        }

        return key switch
        {
            "linear" => pVar,
            "quad-out" or "quadout" => $"({pVar}*(2-{pVar}))",
            "cubic-out" or "cubicout" => $"(1-pow(1-{pVar},3))",
            "quartic-out" or "quarticout" => $"(1-pow(1-{pVar},4))",
            "ease-in-out" or "easeinout" => $"({pVar}*{pVar}*(3-2*{pVar}))",
            _ => isEaseIn ? $"(1-pow(1-{pVar},3))" : $"({pVar}*(2-{pVar}))"
        };
    }

    /// <summary>
    /// Belirtilen video saniyesinde aktif bir zoom/pan durumu varsa koordinat ve ölçek değerini
    /// Cubic-Ease-Out ve Sine-Ease-Out yumuşak geçişleriyle hesaplar.
    /// 
    /// AKILLI ZOOM VE SINIR İHLALİ (BOUNDARY CHECK):
    /// Tıklanan nokta merkez alınarak bir Sanal Sınır Kutusu oluşturulur.
    /// Görüntü büyüdükten sonra kullanıcının faresi bu sınır kutusunun dışına çıkarsa
    /// zoom işlemi pürüzsüzce iptal edilir (Zoom-Out) ve orijinal ekrana dönülür.
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
            
            double targetScale = Math.Max(1.0, e.Scale);
            double viewW = vW / targetScale;
            double viewH = vH / targetScale;
            double halfW = viewW / 2.0;
            double halfH = viewH / 2.0;

            double tx = e.TargetX > 0 ? e.TargetX : defaultCenterX;
            double ty = e.TargetY > 0 ? e.TargetY : defaultCenterY;

            tx = Math.Clamp(tx, halfW, vW - halfW);
            ty = Math.Clamp(ty, halfH, vH - halfH);

            bool isContinuousPanNext = hasNext && Math.Abs(sorted[i + 1].Scale - targetScale) < 0.01;
            
            if (hasNext)
            {
                end = sorted[i + 1].StartTime;
                dur = Math.Max(0.3, end - start);
            }

            bool hasPrev = (i > 0 && start <= (sorted[i - 1].StartTime + sorted[i - 1].Duration + 0.15));
            double prevScale = hasPrev ? sorted[i - 1].Scale : 1.0;
            bool isContinuousPanPrev = hasPrev && Math.Abs(prevScale - targetScale) < 0.01;

            double transIn = hasPrev ? (isContinuousPanPrev ? Math.Min(DefaultTransitionTime, dur * 0.35) : 0.25) : Math.Min(DefaultTransitionTime, dur * 0.30);
            double transOut = hasNext ? (isContinuousPanNext ? 0.0 : 0.25) : Math.Min(DefaultTransitionTime, dur * 0.30);

            double inEnd = start + transIn;
            double outStart = end - transOut;

            // KURAL: Sınır ihlali kontrolünü artık GenerateZoomEffectsFromClicks içinde state machine yapıyor.
            // Bu yüzden buradaki checkStart ve outStart dinamik küçültme mantığını kaldırdık, 
            // sadece üretilen net Duration'lara itimat ediyoruz.


            if (timeSec >= start && timeSec <= end)
            {
                if (e.DragEndTime > 0 && timeSec >= inEnd && timeSec <= e.DragEndTime && moves != null)
                {
                    double boundHalfW = Math.Max(240.0, halfW * 0.75);
                    double boundHalfH = Math.Max(180.0, halfH * 0.75);
                    double simX = e.TargetX;
                    double simY = e.TargetY;
                    foreach (var m in moves.Where(m => m.Timestamp >= inEnd && m.Timestamp <= timeSec))
                    {
                        if (m.X > simX + boundHalfW) simX = m.X - boundHalfW;
                        else if (m.X < simX - boundHalfW) simX = m.X + boundHalfW;

                        if (m.Y > simY + boundHalfH) simY = m.Y - boundHalfH;
                        else if (m.Y < simY - boundHalfH) simY = m.Y + boundHalfH;
                        
                        simX = Math.Clamp(simX, halfW, vW - halfW);
                        simY = Math.Clamp(simY, halfH, vH - halfH);
                    }
                    tx = simX;
                    ty = simY;
                }
                else if (e.DragEndTime > 0 && timeSec > e.DragEndTime)
                {
                    tx = e.TargetX2 > 0 ? e.TargetX2 : e.TargetX;
                    ty = e.TargetY2 > 0 ? e.TargetY2 : e.TargetY;
                    tx = Math.Clamp(tx, halfW, vW - halfW);
                    ty = Math.Clamp(ty, halfH, vH - halfH);
                }

                // 1. ZOOM-IN EVRESİ: Doğrusal olmayan Cubic-Ease-Out animasyonu
                if (timeSec < inEnd && transIn > 0.0001)
                {
                    double p = Math.Clamp((timeSec - start) / transIn, 0.0, 1.0);
                    double ease = ApplyEasing(p, e.Easing);

                    prevScale = hasPrev ? sorted[i - 1].Scale : 1.0;
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

                // 2. HOLD (SABİT ZOOM) EVRESİ
                if (timeSec >= inEnd && timeSec <= outStart)
                {
                    return new ActiveZoomState
                    {
                        Scale = Math.Round(targetScale, 4),
                        TargetX = Math.Round(tx, 2),
                        TargetY = Math.Round(ty, 2)
                    };
                }

                // 3. ZOOM-OUT EVRESİ: Sine-Ease-Out ile pürüzsüz orijinal ekrana dönüş
                if (timeSec > outStart && transOut > 0.0001)
                {
                    double p = Math.Clamp((timeSec - outStart) / transOut, 0.0, 1.0);
                    double ease = ApplyEasing(p, e.Easing);

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
            bool isContinuousPanNext = hasNext && Math.Abs(sorted[i + 1].Scale - e.Scale) < 0.01;

            if (hasNext)
            {
                end = sorted[i + 1].StartTime;
                dur = Math.Max(0.3, end - start);
            }

            bool hasPrev = (i > 0 && start <= (sorted[i - 1].StartTime + sorted[i - 1].Duration + 0.15));
            double prevScale = hasPrev ? sorted[i - 1].Scale : 1.0;
            bool isContinuousPanPrev = hasPrev && Math.Abs(prevScale - e.Scale) < 0.01;

            double transIn = hasPrev ? (isContinuousPanPrev ? Math.Min(DefaultTransitionTime, dur * 0.35) : 0.25) : Math.Min(DefaultTransitionTime, dur * 0.30);
            double transOut = hasNext ? (isContinuousPanNext ? 0.0 : 0.25) : Math.Min(DefaultTransitionTime, dur * 0.30);

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

            string holdX = clampX;
            string holdY = clampY;

            if (e.DragEndTime > 0 && e.TargetX2 > 0 && e.TargetY2 > 0)
            {
                string sDragEnd = e.DragEndTime.ToString("F3", CultureInfo.InvariantCulture);
                string clampX2 = $"max(0,min(({e.TargetX2.ToString("F1", CultureInfo.InvariantCulture)}-(iw/zoom/2)),iw-(iw/zoom)))";
                string clampY2 = $"max(0,min(({e.TargetY2.ToString("F1", CultureInfo.InvariantCulture)}-(ih/zoom/2)),ih-(ih/zoom)))";
                
                string dragProgress = $"min(1, max(0, (in_time-{sInEnd})/max(0.001, {sDragEnd}-{sInEnd})))";
                holdX = $"({clampX} + {dragProgress} * ({clampX2} - {clampX}))";
                holdY = $"({clampY} + {dragProgress} * ({clampY2} - {clampY}))";
            }

            string pIn = $"((in_time-{sStart})/{sTransIn})";
            string easeIn = GetFfmpegEaseExpression(pIn, e.Easing, true);
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
                string easeOut = GetFfmpegEaseExpression(pOut, e.Easing, false);

                zOut = sScale; 
                curX = $"if(between(in_time,{sStart},{sInEnd}),(iw/2-(iw/zoom/2))+({holdX}-(iw/2-(iw/zoom/2)))*{easeIn},if(between(in_time,{sInEnd},{sOutStart}),{holdX},if(between(in_time,{sOutStart},{sEnd}),{holdX}+({clampNextX}-{holdX})*{easeOut},{xExpr})))";
                curY = $"if(between(in_time,{sStart},{sInEnd}),(ih/2-(ih/zoom/2))+({holdY}-(ih/2-(ih/zoom/2)))*{easeIn},if(between(in_time,{sInEnd},{sOutStart}),{holdY},if(between(in_time,{sOutStart},{sEnd}),{holdY}+({clampNextY}-{holdY})*{easeOut},{yExpr})))";
            }
            else
            {
                string pOut = $"(({sEnd}-in_time)/{sTransOut})";
                string easeOut = GetFfmpegEaseExpression(pOut, e.Easing, false);
                zOut = $"1+{sScaleDelta}*{easeOut}";

                curX = $"if(between(in_time,{sStart},{sInEnd}),(iw/2-(iw/zoom/2))+({holdX}-(iw/2-(iw/zoom/2)))*{easeIn},if(between(in_time,{sInEnd},{sOutStart}),{holdX},if(between(in_time,{sOutStart},{sEnd}),(iw/2-(iw/zoom/2))+({holdX}-(iw/2-(iw/zoom/2)))*{easeOut},{xExpr})))";
                curY = $"if(between(in_time,{sStart},{sInEnd}),(ih/2-(ih/zoom/2))+({holdY}-(ih/2-(ih/zoom/2)))*{easeIn},if(between(in_time,{sInEnd},{sOutStart}),{holdY},if(between(in_time,{sOutStart},{sEnd}),(ih/2-(ih/zoom/2))+({holdY}-(ih/2-(ih/zoom/2)))*{easeOut},{yExpr})))";
            }

            string curZ = $"if(between(in_time,{sStart},{sInEnd}),{zIn},if(between(in_time,{sInEnd},{sOutStart}),{sScale},if(between(in_time,{sOutStart},{sEnd}),{zOut},{zExpr})))";

            zExpr = curZ;
            xExpr = curX;
            yExpr = curY;
        }

        return $"zoompan=z='{zExpr}':x='{xExpr}':y='{yExpr}':d=1:s={videoWidth}x{videoHeight}:fps={fps}";
    }
}
