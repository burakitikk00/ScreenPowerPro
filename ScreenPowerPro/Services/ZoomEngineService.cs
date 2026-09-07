using System;
using System.Collections.Generic;
using System.Linq;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Services;

/// <summary>
/// Zoom ve pan animasyonlarının keyframe tabanlı hesaplandığı,
/// yumuşak geçişlerin (easeInOutCubic) uygulandığı ve FFmpeg filtre ifadelerinin
/// üretildiği merkezi zoom motoru servisi.
/// Electron mimarisindeki zoomEngine.ts dosyasının birebir ve genişletilmiş C# karşılığıdır.
/// </summary>
public class ZoomEngineService
{
    // Varsayılan süre ve ölçek sabitleri
    public const double DefaultZoomDuration = 2.0;    // Varsayılan zoom kalma süresi (sn)
    public const double DefaultScale = 1.5;           // Varsayılan zoom yakınlaşma katsayısı
    public const double MinClickGapSec = 0.8;         // Tıklamalar arası minimum ayrım süresi (800ms)
    
    // Keyframe ve yumuşak geçiş zamanlama parametreleri
    public const double PanThresholdSec = 3.0;        // Aynı zoom kümesi (cluster) sayılma eşiği
    public const double ZoomInTimeSec = 0.8;          // Yakınlaşma animasyonu süresi
    public const double ZoomOutTimeSec = 0.8;         // Uzaklaşma animasyonu süresi
    public const double HoldTimeSec = 1.0;            // Odakta bekleme süresi
    public const double PanTimeSec = 0.6;             // İki tıklama arası kayma (pan) süresi

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
    /// Fare tıklama olaylarını analiz ederek otomatik zoom efektleri listesi üretir.
    /// autoZoomMode: "none", "smooth" veya "instant"
    /// </summary>
    public List<ZoomEffect> GenerateZoomEffectsFromClicks(
        IEnumerable<MouseClickEvent> clicks,
        string autoZoomMode = "smooth",
        double defaultScale = DefaultScale,
        double maxVideoDurationSec = 0)
    {
        // Otomatik zoom kapalıysa boş liste döndür
        if (string.Equals(autoZoomMode, "none", StringComparison.OrdinalIgnoreCase))
        {
            return new List<ZoomEffect>();
        }

        // Sadece sol ve sağ basma (down) anlarını filtrele
        var downClicks = clicks
            .Where(c => c.Type == "left_down" || c.Type == "right_down")
            .OrderBy(c => c.Timestamp)
            .ToList();

        var effects = new List<ZoomEffect>();
        double lastTime = -MinClickGapSec;
        int zoomIndex = 1;

        bool isInstant = string.Equals(autoZoomMode, "instant", StringComparison.OrdinalIgnoreCase);

        foreach (var click in downClicks)
        {
            // Çok sık gelen (800ms'den kısa) tıklamaları filtrele
            if (click.Timestamp - lastTime < MinClickGapSec)
            {
                continue;
            }
            lastTime = click.Timestamp;

            double startTimeSec = click.Timestamp;
            string easing = isInstant ? "instant" : "ease-in-out";
            double duration = isInstant ? 0.3 : DefaultZoomDuration;
            double actualStart = Math.Max(0, startTimeSec - 0.1);

            // Maksimum video süresi aşılmasın
            if (maxVideoDurationSec > 0 && actualStart + duration > maxVideoDurationSec)
            {
                duration = Math.Max(0.3, maxVideoDurationSec - actualStart);
            }

            effects.Add(new ZoomEffect
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Name = $"Zoom {zoomIndex++}",
                StartTime = Math.Round(actualStart, 3),
                Duration = Math.Round(duration, 3),
                TargetX = click.X,
                TargetY = click.Y,
                Scale = defaultScale,
                Easing = easing
            });
        }

        return effects;
    }

    /// <summary>
    /// Verilen zoom efektlerini zaman ve yakınlıklarına göre kümeleyip (clustering),
    /// yumuşak geçişli keyframe dizisine dönüştürür.
    /// </summary>
    public List<Keyframe> BuildKeyframes(IReadOnlyList<ZoomEffect> effects)
    {
        var keyframes = new List<Keyframe>();
        if (effects == null || effects.Count == 0) return keyframes;

        // Başlangıç zamanına göre sırala
        var sortedEffects = effects.OrderBy(e => e.StartTime).ToList();

        // Tıklamaları 3 saniyelik eşik değerine göre kümelere ayır
        var clusters = new List<List<ZoomEffect>>();
        var currentCluster = new List<ZoomEffect> { sortedEffects[0] };

        for (int i = 1; i < sortedEffects.Count; i++)
        {
            var prev = sortedEffects[i - 1];
            var curr = sortedEffects[i];

            if (curr.StartTime - prev.StartTime <= PanThresholdSec)
            {
                currentCluster.Add(curr);
            }
            else
            {
                clusters.Add(currentCluster);
                currentCluster = new List<ZoomEffect> { curr };
            }
        }
        clusters.Add(currentCluster);

        // Her küme için keyframe zincirini oluştur
        foreach (var cluster in clusters)
        {
            var first = cluster[0];

            // 1. Zoom Giriş Başlangıcı (Normal 1.0x ölçek)
            keyframes.Add(new Keyframe
            {
                Time = Math.Max(0, first.StartTime - ZoomInTimeSec),
                Scale = 1.0,
                X = first.TargetX,
                Y = first.TargetY
            });

            // 2. Zoom Giriş Tamamlanması (Hedef ölçeğe ulaşıldı)
            keyframes.Add(new Keyframe
            {
                Time = first.StartTime,
                Scale = first.Scale,
                X = first.TargetX,
                Y = first.TargetY
            });

            // 3. Küme içindeki sonraki tıklamalara doğru yumuşak kayma (Pan)
            for (int i = 1; i < cluster.Count; i++)
            {
                var prev = cluster[i - 1];
                var curr = cluster[i];

                double panStartTime = Math.Max(prev.StartTime, curr.StartTime - PanTimeSec);

                // Önceki konumda kal
                keyframes.Add(new Keyframe
                {
                    Time = panStartTime,
                    Scale = prev.Scale,
                    X = prev.TargetX,
                    Y = prev.TargetY
                });

                // Yeni konuma geçiş tamamlandı
                keyframes.Add(new Keyframe
                {
                    Time = curr.StartTime,
                    Scale = curr.Scale,
                    X = curr.TargetX,
                    Y = curr.TargetY
                });
            }

            // 4. Odakta Bekleme (Hold)
            var last = cluster[^1];
            keyframes.Add(new Keyframe
            {
                Time = last.StartTime + HoldTimeSec,
                Scale = last.Scale,
                X = last.TargetX,
                Y = last.TargetY
            });

            // 5. Zoom Çıkış Tamamlanması (Normal 1.0x ölçeğe geri dönüş)
            keyframes.Add(new Keyframe
            {
                Time = last.StartTime + HoldTimeSec + ZoomOutTimeSec,
                Scale = 1.0,
                X = last.TargetX,
                Y = last.TargetY
            });
        }

        // Keyframe'leri zamana göre sırala ve tekilleştir
        keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
        var uniqueKfs = new List<Keyframe>();

        foreach (var kf in keyframes)
        {
            if (uniqueKfs.Count == 0 || uniqueKfs[^1].Time < kf.Time)
            {
                uniqueKfs.Add(kf);
            }
            else if (Math.Abs(uniqueKfs[^1].Time - kf.Time) < 0.0001)
            {
                // Aynı zamanda çakışan keyframe varsa güncelle
                uniqueKfs[^1] = kf;
            }
        }

        return uniqueKfs;
    }

    /// <summary>
    /// Cubic Ease In/Out enterpolasyon eğrisi hesabı.
    /// Başlangıç ve bitişte ivmelenme ve yavaşlama sağlayarak doğal kamera hareketi hissi verir.
    /// </summary>
    public static double EaseInOutCubic(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t < 0.5
            ? 4.0 * t * t * t
            : 1.0 - Math.Pow(-2.0 * t + 2.0, 3.0) / 2.0;
    }

    /// <summary>
    /// Belirtilen video saniyesinde aktif bir zoom/pan durumu varsa koordinat ve ölçek değerini hesaplar.
    /// Editör ekranında MediaPlayer üzerine CSS benzeri canlı transform uygulamak için kullanılır.
    /// </summary>
    public ActiveZoomState? GetActiveZoomAtTime(IReadOnlyList<ZoomEffect> effects, double timeSec)
    {
        if (effects == null || effects.Count == 0) return null;

        var kfs = BuildKeyframes(effects);
        if (kfs.Count < 2) return null;

        // Zaman aralığı dışındaysa normal durum
        if (timeSec <= kfs[0].Time) return null;
        if (timeSec >= kfs[^1].Time) return null;

        for (int i = 0; i < kfs.Count - 1; i++)
        {
            var k1 = kfs[i];
            var k2 = kfs[i + 1];

            if (timeSec >= k1.Time && timeSec <= k2.Time)
            {
                if (Math.Abs(k1.Time - k2.Time) < 0.0001)
                {
                    return new ActiveZoomState { Scale = k2.Scale, TargetX = k2.X, TargetY = k2.Y };
                }

                double progress = (timeSec - k1.Time) / (k2.Time - k1.Time);
                double t = EaseInOutCubic(progress);

                double scale = k1.Scale + (k2.Scale - k1.Scale) * t;
                double targetX = k1.X + (k2.X - k1.X) * t;
                double targetY = k1.Y + (k2.Y - k1.Y) * t;

                return new ActiveZoomState
                {
                    Scale = Math.Round(scale, 4),
                    TargetX = Math.Round(targetX, 2),
                    TargetY = Math.Round(targetY, 2)
                };
            }
        }

        return null;
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
    /// FFmpeg zoompan filtre ifadesini oluşturur.
    /// Video dışa aktarımında doğrudan FFmpeg CLI parametresi olarak kullanılır.
    /// </summary>
    public string BuildZoompanFilter(
        IReadOnlyList<ZoomEffect> effects,
        int videoWidth,
        int videoHeight,
        int fps)
    {
        if (effects == null || effects.Count == 0)
        {
            return $"scale={videoWidth}:{videoHeight}";
        }

        // Zoom ölçek ifadesi (zExpr)
        var zParts = new List<string>();
        foreach (var e in effects)
        {
            double end = e.StartTime + e.Duration;
            zParts.Add($"if(between(t,{e.StartTime:F3},{end:F3}),{e.Scale:F2},1)");
        }
        string zExpr = zParts.Count > 0
            ? zParts.Aggregate((acc, cur) => acc == "1" ? cur : $"if(eq({acc},1),{cur},{acc})")
            : "1";

        // X koordinatı ifadesi (xExpr) - merkezlemeyi hedefe göre clamp eder
        var xParts = new List<string>();
        foreach (var e in effects)
        {
            double end = e.StartTime + e.Duration;
            string vwExpr = $"(iw/{e.Scale:F2})";
            string rawCx = $"({e.TargetX:F1}-{vwExpr}/2)";
            string clampX = $"max(0,min({rawCx},iw-{vwExpr}))";
            xParts.Add($"if(between(t,{e.StartTime:F3},{end:F3}),{clampX},iw/2-(iw/zoom/2))");
        }
        string xExpr = string.Join(":", xParts);

        // Y koordinatı ifadesi (yExpr)
        var yParts = new List<string>();
        foreach (var e in effects)
        {
            double end = e.StartTime + e.Duration;
            string vhExpr = $"(ih/{e.Scale:F2})";
            string rawCy = $"({e.TargetY:F1}-{vhExpr}/2)";
            string clampY = $"max(0,min({rawCy},ih-{vhExpr}))";
            yParts.Add($"if(between(t,{e.StartTime:F3},{end:F3}),{clampY},ih/2-(ih/zoom/2))");
        }
        string yExpr = string.Join(":", yParts);

        return $"zoompan=z='{zExpr}':x='{xExpr}':y='{yExpr}':d=1:s={videoWidth}x{videoHeight}:fps={fps}";
    }
}
