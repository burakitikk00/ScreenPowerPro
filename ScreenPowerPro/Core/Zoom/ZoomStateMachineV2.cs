// =============================================================================
// ScreenPowerPro — ZoomStateMachineV2
// Gelişmiş zoom durum makinesi.
//
// Yeni state: SmoothPanning
//   Zoom içindeyken fare başka yöne gittiğinde ZOOM-OUT YAPILMAZ.
//   Kamera mevcut scale'de kalır, sadece merkez kaydırılır.
//
// Tetikleyiciler → State geçişleri:
//   NewFocusClick (Idle)      → ZoomingIn
//   NewFocusClick (zoomed)    → SmoothPanning (scale sabit)
//   RenewHold                 → hold süresi uzar (state değişmez)
//   FastMove (Holding)        → SmoothPanning
//   FocusLost / timeout       → ZoomingOut (hidden ease-out spring ile)
// =============================================================================

using System;
using System.Drawing;
using ScreenPowerPro.Services;

namespace ScreenPowerPro.Core.Zoom;

/// <summary>
/// Zoom durum makinesi durumları (V2).
/// </summary>
public enum ZoomStateV2
{
    Idle,           // Scale=1, kamera serbest
    ZoomingIn,      // Spring hedef scale'e ulaşıyor
    Holding,        // Sabit zoom, spring-pan pasif
    SmoothPanning,  // Zoom sabit, sadece merkez kaydırılıyor (NO zoom-out döngüsü!)
    ZoomingOut      // Spring 1.0'a dönüyor (hidden ease-out dahil)
}

/// <summary>
/// Durum değişikliği olayı.
/// </summary>
public readonly struct ZoomStateTransition
{
    public ZoomStateV2 From    { get; init; }
    public ZoomStateV2 To      { get; init; }
    public double      TimeMs  { get; init; }
}

/// <summary>
/// Spring-damper tabanlı akıllı zoom durum makinesi.
/// </summary>
public sealed class ZoomStateMachineV2
{
    // --- Konfigürasyon ---
    public double TargetScale        { get; set; } = 1.6;
    public double HoldDurationMs     { get; set; } = 3000.0;   // Hold süresi (ms)
    public double VideoWidth         { get; set; } = 1920.0;
    public double VideoHeight        { get; set; } = 1080.0;
    public string? EasingName        { get; set; } = "Cubic-Out";

    // Pre-click anticipation: spring hedefe tıklamadan bu kadar ms önce yönlenir
    public double AnticipationMs     { get; set; } = 120.0;

    // Pan smoothness: kamera fareyi %60 kadar geriden takip eder
    public double PanFollowRatio     { get; set; } = 0.60;

    // --- Durum ---
    public ZoomStateV2 CurrentState  { get; private set; } = ZoomStateV2.Idle;
    public PointF      FocusPoint    { get; private set; }

    // Durum değişikliği event
    public event Action<ZoomStateTransition>? StateChanged;

    private double _holdExpiryMs = 0;
    private double _stateStartMs = 0;
    private bool   _cameraInitialized = false;

    // İlişkili spring kamera
    private readonly SpringDamperCamera _camera;

    public ZoomStateMachineV2(SpringDamperCamera camera)
    {
        _camera = camera;
    }

    // --- Kamera yardımcı accessor ---
    public CameraSnapshot CameraState => _camera.Snapshot;

    /// <summary>
    /// Filtrelenmiş giriş olayını işler.
    /// </summary>
    public void OnInput(ZoomInputEvent input)
    {
        switch (input.Signal)
        {
            case ZoomInputSignal.NewFocusClick:
                HandleNewFocusClick(input.Position, input.TimestampMs);
                break;

            case ZoomInputSignal.RenewHold:
                // Aynı bölge: sadece hold süresini uzat, milimetrik spring düzeltmesi yap
                if (CurrentState is ZoomStateV2.Holding or ZoomStateV2.ZoomingIn or ZoomStateV2.SmoothPanning)
                {
                    _holdExpiryMs = Math.Max(_holdExpiryMs, input.TimestampMs + HoldDurationMs);
                    // Milimetrik düzeltme: tıklama noktası ile mevcut odak arasında hafif kayma
                    var blended = BlendPositions(FocusPoint, input.Position, 0.15f);
                    FocusPoint = blended;
                    _camera.SetPanTarget(
                        ClampX(blended.X),
                        ClampY(blended.Y));
                }
                break;

            case ZoomInputSignal.FastMove:
                if (CurrentState is ZoomStateV2.Holding or ZoomStateV2.ZoomingIn)
                {
                    // Scale sabit tut, sadece pan hedefini güncelle
                    TransitionTo(ZoomStateV2.SmoothPanning, input.TimestampMs);
                    UpdatePanTarget(input.Position);
                }
                else if (CurrentState == ZoomStateV2.SmoothPanning)
                {
                    UpdatePanTarget(input.Position);
                }
                break;

            case ZoomInputSignal.SlowMove:
                if (CurrentState == ZoomStateV2.SmoothPanning)
                {
                    // Yavaşladı: panning devam et ama geriden takip et
                    UpdatePanTarget(input.Position);
                }
                else if (CurrentState == ZoomStateV2.Holding)
                {
                    // Küçük harekette bile hafif pan uygula (daha doğal his)
                    UpdatePanTarget(input.Position, followRatio: 0.3);
                }
                break;

            case ZoomInputSignal.FocusLost:
                if (CurrentState != ZoomStateV2.Idle && CurrentState != ZoomStateV2.ZoomingOut)
                    TriggerZoomOut(input.TimestampMs);
                break;
        }
    }

    /// <summary>
    /// Her fizik tick'inde çağrılır. Zaman aşımı ve state geçişlerini yönetir.
    /// </summary>
    public void Update(double currentMs)
    {
        switch (CurrentState)
        {
            case ZoomStateV2.ZoomingIn:
                // Kamera hedefe yaklaştı mı?
                if (_camera.IsSettled)
                    TransitionTo(ZoomStateV2.Holding, currentMs);
                // Hold expiry kontrolü (anında geçiş zamanı)
                if (currentMs >= _holdExpiryMs)
                    TriggerZoomOut(currentMs);
                break;

            case ZoomStateV2.Holding:
                if (currentMs >= _holdExpiryMs)
                    TriggerZoomOut(currentMs);
                break;

            case ZoomStateV2.SmoothPanning:
                if (currentMs >= _holdExpiryMs)
                    TriggerZoomOut(currentMs);
                break;

            case ZoomStateV2.ZoomingOut:
                if (_camera.IsSettled)
                    TransitionTo(ZoomStateV2.Idle, currentMs);
                break;

            case ZoomStateV2.Idle:
                break;
        }
    }

    // ----------------------------------------------------------------

    private void HandleNewFocusClick(PointF pos, double timestampMs)
    {
        FocusPoint = pos;
        _holdExpiryMs = timestampMs + AnticipationMs + HoldDurationMs;

        bool alreadyZoomed = CurrentState is ZoomStateV2.ZoomingIn
                                          or ZoomStateV2.Holding
                                          or ZoomStateV2.SmoothPanning;

        if (!alreadyZoomed)
        {
            // İlk zoom: kamerayı başlat
            if (!_cameraInitialized)
            {
                _camera.Initialize(VideoWidth / 2.0, VideoHeight / 2.0, 1.0, EasingName);
                _cameraInitialized = true;
            }
            _camera.UpdateSpringConfig(EasingName);
            _camera.SetTarget(ClampX(pos.X), ClampY(pos.Y), TargetScale);
            TransitionTo(ZoomStateV2.ZoomingIn, timestampMs);
        }
        else
        {
            // Zaten zoom içindeyiz → SmoothPanning (zoom-out/in döngüsü YOK)
            _camera.SetPanTarget(ClampX(pos.X), ClampY(pos.Y));
            if (CurrentState != ZoomStateV2.SmoothPanning)
                TransitionTo(ZoomStateV2.SmoothPanning, timestampMs);
        }
    }

    private void UpdatePanTarget(PointF rawPos, double followRatio = -1)
    {
        double ratio = followRatio < 0 ? PanFollowRatio : followRatio;
        var snap = _camera.Snapshot;

        // Kameranın mevcut merkezini %followRatio oranında hedefe çek
        double targetX = snap.CenterX + (rawPos.X - snap.CenterX) * ratio;
        double targetY = snap.CenterY + (rawPos.Y - snap.CenterY) * ratio;

        _camera.SetPanTarget(ClampX(targetX), ClampY(targetY));
    }

    private void TriggerZoomOut(double currentMs)
    {
        // Spring'i 1.0 scale'e, merkeze döndür
        _camera.SetTarget(VideoWidth / 2.0, VideoHeight / 2.0, 1.0);
        TransitionTo(ZoomStateV2.ZoomingOut, currentMs);
    }

    private void TransitionTo(ZoomStateV2 newState, double currentMs)
    {
        if (CurrentState == newState) return;

        var transition = new ZoomStateTransition
        {
            From   = CurrentState,
            To     = newState,
            TimeMs = currentMs
        };

        CurrentState  = newState;
        _stateStartMs = currentMs;
        StateChanged?.Invoke(transition);
    }

    private static PointF BlendPositions(PointF a, PointF b, float t) =>
        new((float)(a.X + (b.X - a.X) * t),
            (float)(a.Y + (b.Y - a.Y) * t));

    private double ClampX(double x)
    {
        double halfW = (VideoWidth  / TargetScale) / 2.0;
        return Math.Clamp(x, halfW, VideoWidth  - halfW);
    }

    private double ClampY(double y)
    {
        double halfH = (VideoHeight / TargetScale) / 2.0;
        return Math.Clamp(y, halfH, VideoHeight - halfH);
    }
}
