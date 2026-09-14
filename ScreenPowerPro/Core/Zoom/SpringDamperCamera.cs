// =============================================================================
// ScreenPowerPro — SpringDamperCamera
// Fizik tabanlı yay-amortisör kamera sistemi.
//
// Matematiksel Temel (Critically-Damped Spring):
//   a  = (target − pos) × stiffness − velocity × damping
//   v += a × dt
//   pos += v × dt
//
// Bu formül doğal bir ease-in + ease-out sağlar:
//   • Başlangıçta ivme sıfırdan büyür   → hidden ease-in
//   • Hedefe yaklaştıkça ivme azalır    → hidden ease-out
//   • Hedefte velocity → 0              → sarsıntısız durma
// =============================================================================

using System;
using System.Runtime.CompilerServices;

namespace ScreenPowerPro.Core.Zoom;

/// <summary>
/// Bezier kontrol noktalarından spring-damper parametre çifti türetir.
/// </summary>
public readonly struct SpringConfig
{
    public double Stiffness { get; init; }
    public double Damping   { get; init; }

    public SpringConfig(double stiffness, double damping)
    {
        Stiffness = stiffness;
        Damping   = damping;
    }

    /// <summary>
    /// cubic-bezier(x1,y1,x2,y2) → (Stiffness, Damping) dönüşümü.
    /// y1 büyüdükçe daha sert yay; x2 büyüdükçe daha güçlü amortisör.
    /// </summary>
    public static SpringConfig FromBezier(double x1, double y1, double x2, double y2)
    {
        // y1: ilk kontrol noktasının dikeyi → tepki hızını belirler (stiffness ~ 80..280)
        double stiffness = 80.0 + Math.Clamp(y2, 0.0, 1.5) * 200.0;

        // x2: ikinci kontrol noktasının yatayı → söndürme şiddetini belirler (damping ~ 10..35)
        double damping   = 10.0 + Math.Clamp(x1 + (1.0 - x2), 0.0, 1.0) * 25.0;

        return new SpringConfig(stiffness, damping);
    }

    // Ön tanımlı ayarlar
    public static SpringConfig CubicOut    => new(180.0, 22.0);
    public static SpringConfig QuadOut     => new(140.0, 18.0);
    public static SpringConfig QuarticOut  => new(240.0, 28.0);
    public static SpringConfig EaseInOut   => new(120.0, 20.0);
    public static SpringConfig Linear      => new(200.0, 30.0); // overdamped

    /// <summary>Easing ismine göre ön tanımlı config döner.</summary>
    public static SpringConfig FromEasingName(string? easing)
    {
        if (string.IsNullOrWhiteSpace(easing)) return CubicOut;
        string key = easing.Trim().ToLowerInvariant().Replace(" ", "-").Replace("_", "-");

        if (key.StartsWith("cubic-bezier("))
        {
            // cubic-bezier(0.22,0.61,0.36,1.00) → parse
            try
            {
                string inner = key[13..^1];
                var parts = inner.Split(',');
                if (parts.Length == 4 &&
                    double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double x1) &&
                    double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double y1) &&
                    double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double x2) &&
                    double.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double y2))
                {
                    return FromBezier(x1, y1, x2, y2);
                }
            }
            catch { /* fallback */ }
        }

        return key switch
        {
            "linear"                          => Linear,
            "quad-out" or "quadout"           => QuadOut,
            "cubic-out" or "cubicout"         => CubicOut,
            "quartic-out" or "quarticout"     => QuarticOut,
            "ease-in-out" or "easeinout"      => EaseInOut,
            _                                 => CubicOut
        };
    }
}

/// <summary>
/// Kameranın anlık görüntüsü (double-buffer için kullanılır).
/// </summary>
public readonly struct CameraSnapshot
{
    public double Scale  { get; init; }
    public double CenterX { get; init; }
    public double CenterY { get; init; }

    public static readonly CameraSnapshot Identity = new() { Scale = 1.0, CenterX = 960, CenterY = 540 };
}

/// <summary>
/// Fizik tabanlı spring-damper kamera.
/// • Her eksende bağımsız yay: Scale, X, Y
/// • dt bağımsız → FPS fark etmez
/// • Lock-free volatile snapshot → render thread'i bloke etmez
/// • Bezier easing → spring parametresine otomatik mapleme
/// </summary>
public sealed class SpringDamperCamera
{
    // --- Yay parametreleri (easing'den türetilir) ---
    private SpringConfig _scaleSpring = SpringConfig.CubicOut;
    private SpringConfig _posSpring   = SpringConfig.CubicOut;

    // --- Pozisyon fizik durumu ---
    private double _posX,      _posY,      _posScale;
    private double _velX,      _velY,      _velScale;
    private double _targetX,   _targetY,   _targetScale;

    // --- Lock-free snapshot (render thread okur, physics thread yazar)
    // volatile struct mümkün değil (CS0677) → object referans + Interlocked kullanılır
    private object _snapshotRef = (object)CameraSnapshot.Identity;

    // Settle toleransı: velocity + mesafe bu eşiğin altındaysa fizik durur
    private const double SettleTolerance = 0.0005;
    private bool _settled = true;

    /// <summary>Render thread'inin okuduğu anlık kamera durumu.</summary>
    public CameraSnapshot Snapshot => (CameraSnapshot)_snapshotRef;

    /// <summary>Kamera tamamen durmuş mu?</summary>
    public bool IsSettled => _settled;

    /// <summary>
    /// Başlangıç konumunu ve easing'e göre spring parametrelerini ayarlar.
    /// Genellikle kamera ilk oluşturulduğunda veya reset edildiğinde çağrılır.
    /// </summary>
    public void Initialize(double centerX, double centerY, double scale, string? easingName = null)
    {
        _posX     = _targetX     = centerX;
        _posY     = _targetY     = centerY;
        _posScale = _targetScale = scale;
        _velX = _velY = _velScale = 0;
        _settled = true;
        UpdateSpringConfig(easingName);
        PublishSnapshot();
    }

    /// <summary>
    /// Hedef pozisyonu ve ölçeği günceller (spring otomatik yumuşatır).
    /// </summary>
    public void SetTarget(double centerX, double centerY, double scale)
    {
        _targetX     = centerX;
        _targetY     = centerY;
        _targetScale = scale;
        _settled     = false;
    }

    /// <summary>
    /// Sadece panning hedefini günceller (scale değişmez).
    /// </summary>
    public void SetPanTarget(double centerX, double centerY)
    {
        _targetX = centerX;
        _targetY = centerY;
        _settled = false;
    }

    /// <summary>
    /// Spring konfigürasyonunu easing ismine göre günceller.
    /// </summary>
    public void UpdateSpringConfig(string? easingName)
    {
        _scaleSpring = SpringConfig.FromEasingName(easingName);
        // Panning için biraz daha yumuşak (daha az stiffness) ayarla
        _posSpring = new SpringConfig(
            _scaleSpring.Stiffness * 0.7,
            _scaleSpring.Damping   * 0.9);
    }

    /// <summary>
    /// Fizik simülasyonu adımı. Her render tick'inde dt (saniye) ile çağrılır.
    /// Thread-safe değildir; sadece fizik thread'inden çağrılmalıdır.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Step(double dt)
    {
        if (_settled) return;

        // dt'yi makul sınırlar içinde tut (kare atlamaları spike yaratmasın)
        dt = Math.Clamp(dt, 0.0001, 0.05);

        bool xSettled     = StepAxis(ref _posX, ref _velX, _targetX, _posSpring,   dt);
        bool ySettled     = StepAxis(ref _posY, ref _velY, _targetY, _posSpring,   dt);
        bool scaleSettled = StepAxis(ref _posScale, ref _velScale, _targetScale, _scaleSpring, dt);

        _settled = xSettled && ySettled && scaleSettled;
        PublishSnapshot();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool StepAxis(ref double pos, ref double vel, double target,
                                  SpringConfig cfg, double dt)
    {
        double displacement = target - pos;
        double acceleration = displacement * cfg.Stiffness - vel * cfg.Damping;
        vel += acceleration * dt;
        pos += vel * dt;

        bool settled = Math.Abs(vel) < SettleTolerance && Math.Abs(displacement) < SettleTolerance;
        if (settled) { pos = target; vel = 0; }
        return settled;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PublishSnapshot()
    {
        var snap = new CameraSnapshot
        {
            Scale   = Math.Max(1.0, _posScale),
            CenterX = _posX,
            CenterY = _posY
        };
        // Atomic reference swap — lock-free, thread-safe read from render thread
        System.Threading.Interlocked.Exchange(ref _snapshotRef, (object)snap);
    }

    /// <summary>
    /// Kamerayı anında hedef pozisyona snap eder (animasyon olmadan).
    /// </summary>
    public void SnapToTarget()
    {
        _posX = _targetX; _velX = 0;
        _posY = _targetY; _velY = 0;
        _posScale = _targetScale; _velScale = 0;
        _settled = true;
        PublishSnapshot();
    }
}
