// =============================================================================
// ScreenPowerPro — ZoomInputProcessor
// Uzamsal Debounce + Hız Filtresi
//
// Aynı bölgeye ardışık tıklamaları → "renew hold" sinyaline dönüştürür.
// Yüksek hız → smooth pan tetikler, gereksiz zoom-out/in döngüsü yok.
// =============================================================================

using System;
using System.Drawing;

namespace ScreenPowerPro.Core.Zoom;

/// <summary>
/// ZoomStateMachineV2'ye iletilen filtrelenmiş giriş sinyali.
/// </summary>
public enum ZoomInputSignal
{
    /// <summary>Yeni, farklı bir bölgeye tıklama (zoom-in veya pan hedefi).</summary>
    NewFocusClick,

    /// <summary>Aynı bölgede tekrar tıklama → sadece hold süresini uzat.</summary>
    RenewHold,

    /// <summary>Fare yeterince hızlı hareket etti → smooth pan moduna geç.</summary>
    FastMove,

    /// <summary>Normal hareket → sadece pan hedefini güncelle.</summary>
    SlowMove,

    /// <summary>Odak dağıldı (pencere kapandı, ekran dışına çıkıldı).</summary>
    FocusLost
}

/// <summary>
/// Filtrelenmiş giriş olayı.
/// </summary>
public readonly struct ZoomInputEvent
{
    public ZoomInputSignal Signal  { get; init; }
    public PointF          Position { get; init; }
    public double          TimestampMs { get; init; }
}

/// <summary>
/// Ham fare girişlerini filtreleyen katman.
/// — Spatial threshold: aynı bölge yarıçapı içindeki tıklamalar → RenewHold
/// — Temporal debounce: çok kısa sürede aynı pozisyon → yok say
/// — Velocity gate: px/ms > eşik → FastMove
/// </summary>
public sealed class ZoomInputProcessor
{
    // --- Konfigürasyon ---

    /// <summary>Aynı odak noktası sayılacak mesafe (piksel).</summary>
    public double SpatialThresholdPx { get; set; } = 80.0;

    /// <summary>Çok hızlı tıklamaları birleştirme zaman penceresi (ms).</summary>
    public double TemporalDebounceMs { get; set; } = 80.0;

    /// <summary>Bu hızın üstündeki fare hareketi → FastMove (px/ms).</summary>
    public double FastMoveThresholdPxPerMs { get; set; } = 3.0;  // ≈3000 px/sn

    // --- İç durum ---
    private PointF _lastClickPos;
    private double _lastClickTimeMs = double.MinValue;
    private PointF _lastMovePos;
    private double _lastMoveTimeMs = double.MinValue;
    private bool   _hasLastClick = false;

    /// <summary>
    /// Bir tıklama olayını filtreler ve uygun sinyali döner.
    /// </summary>
    public ZoomInputEvent ProcessClick(float x, float y, double timestampMs)
    {
        var pos = new PointF(x, y);

        bool isSameRegion = _hasLastClick &&
                            Distance(_lastClickPos, pos) < SpatialThresholdPx;

        bool isTemporalDebounce = (timestampMs - _lastClickTimeMs) < TemporalDebounceMs;

        _lastClickPos    = pos;
        _lastClickTimeMs = timestampMs;
        _hasLastClick    = true;

        var signal = (isSameRegion || isTemporalDebounce)
            ? ZoomInputSignal.RenewHold
            : ZoomInputSignal.NewFocusClick;

        return new ZoomInputEvent { Signal = signal, Position = pos, TimestampMs = timestampMs };
    }

    /// <summary>
    /// Bir hareket olayını filtreler ve uygun sinyali döner.
    /// </summary>
    public ZoomInputEvent ProcessMove(float x, float y, double timestampMs)
    {
        var pos = new PointF(x, y);

        ZoomInputSignal signal = ZoomInputSignal.SlowMove;

        if (_lastMoveTimeMs > double.MinValue)
        {
            double dt = timestampMs - _lastMoveTimeMs;
            if (dt > 0.0001)
            {
                double dist  = Distance(_lastMovePos, pos);
                double speed = dist / dt; // px/ms
                if (speed > FastMoveThresholdPxPerMs)
                    signal = ZoomInputSignal.FastMove;
            }
        }

        _lastMovePos    = pos;
        _lastMoveTimeMs = timestampMs;

        return new ZoomInputEvent { Signal = signal, Position = pos, TimestampMs = timestampMs };
    }

    /// <summary>Odak kayıp sinyali üretir.</summary>
    public ZoomInputEvent FocusLost(double timestampMs) =>
        new() { Signal = ZoomInputSignal.FocusLost, Position = _lastClickPos, TimestampMs = timestampMs };

    /// <summary>İşlemciyi sıfırla (yeni kayıt başlarken).</summary>
    public void Reset()
    {
        _hasLastClick    = false;
        _lastClickTimeMs = double.MinValue;
        _lastMoveTimeMs  = double.MinValue;
    }

    private static double Distance(PointF a, PointF b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
