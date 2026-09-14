using System;
using System.Collections.Generic;
using System.Numerics;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Services;

/// <summary>
/// Editör önizlemesi için sinematik kamera kontrolcüsü.
/// 4 algoritmayı tek sınıfta toplar:
///   1. Direct Target Zoom  — Aynı E(t) eğrisi ile scale + pan senkronizasyonu
///   2. Dynamic Zoom-Out    — Fare hız vektörü ile zoom-out sırasında dinamik merkez
///   3. Idle Timeout        — 2 saniye hareketsizlikte otomatik zoom-out
///   4. Spring-Damper       — Üst üste tıklamalarda yumuşak geçiş (fizik tabanlı)
///
/// Kullanım: CompositionTarget.Rendering veya DispatcherTimer (16ms) döngüsünden
/// her karede <see cref="Update"/> çağrılır, dönen Scale/Position değeri
/// VideoTransform'a uygulanır.
/// </summary>
public class CinematicCameraController
{
    // =========================================================================
    // Algoritma 3 — Idle Timeout sabitleri
    // =========================================================================
    private const double IdleThresholdPx = 5.0;   // ε — anlamlı hareket eşiği (piksel)
    private const double IdleTimeoutSec  = 2.0;   // T_idle — zoom-out tetikleme süresi

    // =========================================================================
    // Algoritma 4 — Spring-Damper parametreleri
    // =========================================================================
    /// <summary>
    /// Yay sertliği (k). Yüksek değer → daha hızlı hedefe yaklaşma.
    /// Önerilen aralık: 80–300.
    /// </summary>
    public float SpringStiffness { get; set; } = 160f;

    /// <summary>
    /// Sönümleme katsayısı (c). Yüksek değer → daha az yalpalama (overdamped).
    /// Kritik sönümleme: c = 2 * sqrt(k * m), m=1 için c ≈ 25.3 (k=160).
    /// </summary>
    public float SpringDamping { get; set; } = 22f;

    // =========================================================================
    // Fizik durumu (Spring-Damper)
    // =========================================================================
    private Vector2 _camPosition;    // P_cam — anlık kamera konumu (video px)
    private Vector2 _camVelocity;    // V_cam — anlık kamera hızı (px/sn)
    private float   _camScale = 1f;  // Anlık ölçek
    private float   _scaleVelocity;  // Ölçek değişim hızı

    private Vector2 _targetPosition; // P_future — Spring hedefi
    private float   _targetScale = 1f;

    // =========================================================================
    // Idle Timeout durumu
    // =========================================================================
    private double _lastSignificantMoveX;
    private double _lastSignificantMoveY;
    private double _idleAccumulatedSec;

    /// <summary>
    /// True olduğunda idle timer dolmuş — çağıran kodu zoom-out tetiklemeli.
    /// </summary>
    public bool IsIdleTriggered { get; private set; }

    // =========================================================================
    // Algoritma 2 — Dinamik zoom-out fare hız istatistikleri
    // =========================================================================
    private double _lastMouseX;
    private double _lastMouseY;
    private double _lastMouseTime = -1;

    // =========================================================================
    // Başlatma
    // =========================================================================

    /// <summary>
    /// Kamerayı verilen video merkezi koordinatına ve ölçek=1'e sıfırlar.
    /// Yeni proje açıldığında veya editöre girildiğinde çağrılmalıdır.
    /// </summary>
    public void Initialize(double videoWidth, double videoHeight)
    {
        var center = new Vector2((float)(videoWidth / 2.0), (float)(videoHeight / 2.0));
        _camPosition     = center;
        _targetPosition  = center;
        _camVelocity     = Vector2.Zero;
        _camScale        = 1f;
        _scaleVelocity   = 0f;
        _targetScale     = 1f;

        _idleAccumulatedSec      = 0;
        _lastSignificantMoveX    = videoWidth  / 2.0;
        _lastSignificantMoveY    = videoHeight / 2.0;
        IsIdleTriggered          = false;
    }

    // =========================================================================
    // Algoritma 3 — Idle Timeout Algılama
    // =========================================================================

    /// <summary>
    /// Her render karesinde fare konumunu kontrol eder.
    ///
    /// Matematiksel model:
    ///   d = sqrt((Xcurrent - Xlast)² + (Ycurrent - Ylast)²)
    ///   d > ε  → timer sıfırla, M_last = M_current
    ///   d ≤ ε  → timer += Δt
    ///   T ≥ 2.0s → IsIdleTriggered = true
    /// </summary>
    /// <param name="mouseX">Fare X koordinatı (video piksel uzayında)</param>
    /// <param name="mouseY">Fare Y koordinatı (video piksel uzayında)</param>
    /// <param name="deltaT">Geçen süre (saniye)</param>
    public void UpdateIdleDetection(double mouseX, double mouseY, double deltaT)
    {
        double dx = mouseX - _lastSignificantMoveX;
        double dy = mouseY - _lastSignificantMoveY;
        double distSq = dx * dx + dy * dy;

        if (distSq > IdleThresholdPx * IdleThresholdPx) // > ε²
        {
            // Fare hareket etti — timer sıfırla
            _idleAccumulatedSec  = 0;
            _lastSignificantMoveX = mouseX;
            _lastSignificantMoveY = mouseY;
            IsIdleTriggered      = false;
        }
        else
        {
            _idleAccumulatedSec += deltaT;
            if (_idleAccumulatedSec >= IdleTimeoutSec)
            {
                IsIdleTriggered = true;
            }
        }
    }

    /// <summary>
    /// Idle timer'ı sıfırla (örn. yeni tıklama geldiğinde).
    /// </summary>
    public void ResetIdle()
    {
        _idleAccumulatedSec = 0;
        IsIdleTriggered     = false;
    }

    // =========================================================================
    // Algoritma 2 — Fare hız vektörü (Zoom-Out dinamik merkez için)
    // =========================================================================

    /// <summary>
    /// Mevcut fare hızını (px/sn) günceller ve döner.
    /// V_mouse = (M_current - M_previous) / Δt
    /// </summary>
    public (double Vx, double Vy) UpdateAndGetMouseVelocity(
        double mouseX, double mouseY, double currentTimeSec)
    {
        double vx = 0, vy = 0;

        if (_lastMouseTime > 0)
        {
            double dt = currentTimeSec - _lastMouseTime;
            if (dt > 1e-6 && dt < 0.5) // Makul aralık
            {
                vx = (mouseX - _lastMouseX) / dt;
                vy = (mouseY - _lastMouseY) / dt;

                // Aşırı hızları sınırla (max 3000 px/sn)
                const double MaxV = 3000.0;
                vx = Math.Clamp(vx, -MaxV, MaxV);
                vy = Math.Clamp(vy, -MaxV, MaxV);
            }
        }

        _lastMouseX    = mouseX;
        _lastMouseY    = mouseY;
        _lastMouseTime = currentTimeSec;

        return (vx, vy);
    }

    // =========================================================================
    // Algoritma 4 — Spring-Damper Hedef Güncelleme
    // =========================================================================

    /// <summary>
    /// Yeni zoom hedefini ayarlar. Spring-Damper mekaniği sayesinde
    /// eski hız ve momentum korunur — keskin geçiş olmaz.
    ///
    /// Üst üste tıklamalarda eski animasyonu iptal etmeye GEREK YOK;
    /// sadece bu metodu çağırın.
    /// </summary>
    /// <param name="targetX">Hedef X koordinatı (video piksel uzayı)</param>
    /// <param name="targetY">Hedef Y koordinatı (video piksel uzayı)</param>
    /// <param name="targetScale">Hedef ölçek (1.0 = normal)</param>
    public void SetTarget(double targetX, double targetY, double targetScale)
    {
        _targetPosition = new Vector2((float)targetX, (float)targetY);
        _targetScale    = (float)Math.Clamp(targetScale, 1.0, 5.0);
        ResetIdle();
    }

    /// <summary>
    /// Zoom-out için merkezi sıfırlar (idle timeout veya zoom efekti bitti).
    /// </summary>
    /// <param name="centerX">Video merkezi X</param>
    /// <param name="centerY">Video merkezi Y</param>
    public void SetTargetCenter(double centerX, double centerY)
    {
        _targetPosition = new Vector2((float)centerX, (float)centerY);
        _targetScale    = 1f;
    }

    // =========================================================================
    // Algoritma 4 — Spring-Damper Fizik Güncellemesi
    // =========================================================================

    /// <summary>
    /// Her render karesinde çağrılır. Spring-Damper fiziğini uygular.
    ///
    /// Matematiksel model (her Δt'de):
    ///   F      = k * (P_future - P_cam) - c * V_cam
    ///   V_cam  = V_cam + F * Δt
    ///   P_cam  = P_cam + V_cam * Δt
    ///
    /// Dönen değerleri VideoTransform'a (ScaleX/Y, TranslateX/Y) uygulayın.
    /// </summary>
    /// <param name="deltaT">Kare süresi (saniye). Clamp [0, 0.05] uygulanır.</param>
    /// <returns>(Position, Scale) — video piksel uzayında kamera konumu ve ölçeği</returns>
    public (Vector2 Position, float Scale) Update(double deltaT)
    {
        float dt = (float)Math.Clamp(deltaT, 0.0, 0.05); // Max 50ms adım (tutarlılık)
        if (dt < 1e-6f) return (_camPosition, _camScale);

        // --- Pozisyon yayı ---
        Vector2 posForce = SpringStiffness * (_targetPosition - _camPosition)
                         - SpringDamping   * _camVelocity;
        _camVelocity += posForce * dt;
        _camPosition += _camVelocity * dt;

        // --- Ölçek yayı (1D) ---
        float scaleForce = SpringStiffness * (_targetScale - _camScale)
                         - SpringDamping   * _scaleVelocity;
        _scaleVelocity += scaleForce * dt;
        _camScale      += _scaleVelocity * dt;
        _camScale = Math.Clamp(_camScale, 1f, 5f);

        return (_camPosition, _camScale);
    }

    // =========================================================================
    // Algoritma 4 — Look-Ahead Hedef Belirleme (Timeline verisi ile)
    // =========================================================================

    /// <summary>
    /// Timeline look-ahead: şu anki video zamanından <paramref name="lookAheadSec"/>
    /// saniye ilerisindeki zoom durumunu okur ve Spring hedefini günceller.
    ///
    /// Bu sayede kamera, zoom efektinin başlamasından önce hedefe doğru
    /// süzülmeye başlar → "anticipation" (önceden algılama) hissi.
    /// </summary>
    /// <param name="engine">ZoomEngineService örneği</param>
    /// <param name="effects">Aktif zoom efektleri listesi</param>
    /// <param name="currentTimeSec">Şu anki oynatma zamanı (saniye)</param>
    /// <param name="videoWidth">Kaynak video genişliği</param>
    /// <param name="videoHeight">Kaynak video yüksekliği</param>
    /// <param name="lookAheadSec">İleri bakma süresi (varsayılan 200ms)</param>
    /// <param name="mouseMoves">Fare hareketi verisi (dinamik zoom-out için)</param>
    public void UpdateLookAheadFromTimeline(
        ZoomEngineService engine,
        IReadOnlyList<ZoomEffect>? effects,
        double currentTimeSec,
        double videoWidth,
        double videoHeight,
        double lookAheadSec = 0.200,
        IReadOnlyList<MouseMoveEvent>? mouseMoves = null)
    {
        if (effects == null || effects.Count == 0)
        {
            SetTargetCenter(videoWidth / 2.0, videoHeight / 2.0);
            return;
        }

        double futureSec = currentTimeSec + lookAheadSec;
        double cx = videoWidth  / 2.0;
        double cy = videoHeight / 2.0;

        // Fare koordinatını telemetriden interpolasyon ile bul
        double cursorX = -1, cursorY = -1;
        if (mouseMoves != null && mouseMoves.Count > 0)
        {
            var pt = ZoomEngineService.GetInterpolatedCursorPosition(mouseMoves, futureSec);
            if (pt.HasValue)
            {
                cursorX = pt.Value.X;
                cursorY = pt.Value.Y;
            }
        }

        var futureState = engine.GetActiveZoomAtTime(
            effects,
            futureSec,
            cx, cy,
            cursorX, cursorY,
            mouseMoves);

        if (futureState != null && futureState.Scale > 1.01)
        {
            SetTarget(futureState.TargetX, futureState.TargetY, futureState.Scale);
        }
        else
        {
            // Algoritma 2: Zoom-out sırasında fare hızını kullan
            if (mouseMoves != null && cursorX > 0)
            {
                double prevSec = Math.Max(0, currentTimeSec - 0.05); // 50ms pencere
                var prevPt = ZoomEngineService.GetInterpolatedCursorPosition(mouseMoves, prevSec);
                if (prevPt.HasValue)
                {
                    double dtV = currentTimeSec - prevSec;
                    if (dtV > 1e-6)
                    {
                        double vx = (cursorX - prevPt.Value.X) / dtV;
                        double vy = (cursorY - prevPt.Value.Y) / dtV;

                        // Sınırla (max 2000 px/sn tahmin payı)
                        vx = Math.Clamp(vx, -2000.0, 2000.0);
                        vy = Math.Clamp(vy, -2000.0, 2000.0);

                        // k_tahmin = 0.25sn: kameranın farenin 250ms ilerisini tahmin etmesi
                        const double kLookAhead = 0.25;
                        double predictedX = Math.Clamp(cursorX + vx * kLookAhead, 0, videoWidth);
                        double predictedY = Math.Clamp(cursorY + vy * kLookAhead, 0, videoHeight);

                        // Zoom-out sırasında kamera merkeze dönerken fareyi de takip etsin
                        // Ağırlıklı ortalama: %70 merkez, %30 fare tahmini
                        double blendX = cx * 0.70 + predictedX * 0.30;
                        double blendY = cy * 0.70 + predictedY * 0.30;

                        SetTargetCenter(blendX, blendY);
                        return;
                    }
                }
            }

            SetTargetCenter(cx, cy);
        }
    }

    // =========================================================================
    // Yardımcı Özellikler
    // =========================================================================

    /// <summary>Kameranın anlık konumu (video piksel uzayı)</summary>
    public Vector2 CurrentPosition => _camPosition;

    /// <summary>Kameranın anlık ölçeği</summary>
    public float CurrentScale => _camScale;

    /// <summary>Hedef konum</summary>
    public Vector2 TargetPosition => _targetPosition;

    /// <summary>Hedef ölçek</summary>
    public float TargetScale => _targetScale;

    /// <summary>
    /// Kameranın hedefe yeterince yakın olup olmadığını kontrol eder.
    /// Gereksiz render çağrılarından kaçınmak için kullanılabilir.
    /// </summary>
    public bool IsSettled(double posThreshold = 0.5, double scaleThreshold = 0.005)
    {
        bool posOk   = Vector2.Distance(_camPosition, _targetPosition) < (float)posThreshold;
        bool scaleOk = Math.Abs(_camScale - _targetScale) < (float)scaleThreshold;
        bool velOk   = _camVelocity.Length() < 0.5f && Math.Abs(_scaleVelocity) < 0.001f;
        return posOk && scaleOk && velOk;
    }
}
