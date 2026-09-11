// ==============================================================================
// ScreenPowerPro - Senkronizasyon, Lerp Enterpolasyonu & Akıllı Zoom Mimarisi
// ==============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using ScreenPowerPro.Services;

namespace ScreenPowerPro.Core.Tracking
{
    /// <summary>
    /// Fare olay türleri
    /// </summary>
    public enum MouseEventType
    {
        Move,
        LeftDown,
        LeftUp,
        RightDown,
        RightUp,
        DoubleClick,
        Scroll
    }

    /// <summary>
    /// Milisaniye hassasiyetli zaman damgalı fare verisi modeli.
    /// </summary>
    public struct MouseFrameData
    {
        /// <summary>
        /// Kaydın başlangıcından itibaren geçen süre (milisaniye).
        /// </summary>
        public double TimestampMs { get; set; }

        /// <summary>
        /// Ekran koordinatları (X, Y)
        /// </summary>
        public float X { get; set; }
        public float Y { get; set; }

        /// <summary>
        /// Gerçekleşen olay
        /// </summary>
        public MouseEventType EventType { get; set; }

        public MouseFrameData(double timestampMs, float x, float y, MouseEventType eventType = MouseEventType.Move)
        {
            TimestampMs = timestampMs;
            X = x;
            Y = y;
            EventType = eventType;
        }

        public override string ToString()
        {
            return $"[{TimestampMs:F1}ms] {EventType} @ ({X:F1}, {Y:F1})";
        }
    }

    // ==============================================================================
    // 1. KAYIT MOTORU (Milisaniye Tabanlı & Kayıt Öncesi Temizleme)
    // ==============================================================================

    public enum RecordingState
    {
        Idle,
        Preparing,
        Recording,
        Paused,
        Stopped
    }

    public class PreciseMouseRecorder
    {
        private readonly List<MouseFrameData> _recordedEvents = new();
        private readonly Stopwatch _stopwatch = new();
        private RecordingState _state = RecordingState.Idle;
        private PointF _lastPosition = new(-1, -1);

        public IReadOnlyList<MouseFrameData> RecordedEvents => _recordedEvents;
        public RecordingState State => _state;
        public double ElapsedMilliseconds => _stopwatch.Elapsed.TotalMilliseconds;

        /// <summary>
        /// Kayıt hazırlığı: Önceki tamponları temizler, kayıt öncesi yanlış veri girişini engeller.
        /// </summary>
        public void Prepare()
        {
            _state = RecordingState.Preparing;
            _recordedEvents.Clear();
            _stopwatch.Reset();
            _lastPosition = new PointF(-1, -1);
        }

        /// <summary>
        /// Video karesi yakalanmaya başladığı anda çağrılmalıdır.
        /// </summary>
        public void StartRecording()
        {
            _recordedEvents.Clear(); // Kayıt öncesi sızan fare hareketlerini kesin olarak temizle
            _stopwatch.Restart();
            _state = RecordingState.Recording;
        }

        public void PauseRecording()
        {
            if (_state == RecordingState.Recording)
            {
                _stopwatch.Stop();
                _state = RecordingState.Paused;
            }
        }

        public void ResumeRecording()
        {
            if (_state == RecordingState.Paused)
            {
                _stopwatch.Start();
                _state = RecordingState.Recording;
            }
        }

        public void StopRecording()
        {
            _stopwatch.Stop();
            _state = RecordingState.Stopped;
        }

        /// <summary>
        /// Windows Mouse Hook veya yüksek frekanslı sayaçtan gelen fare verisini kaydeder.
        /// </summary>
        public void OnRawMouseInput(float x, float y, MouseEventType eventType)
        {
            if (_state != RecordingState.Recording)
                return;

            double elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;

            // Veri optimizasyonu: Fare hareket etmediyse ve ayrık bir olay (tıklama vb.) yoksa gereksiz kayıt oluşturma
            if (eventType == MouseEventType.Move)
            {
                float dx = Math.Abs(x - _lastPosition.X);
                float dy = Math.Abs(y - _lastPosition.Y);
                if (dx < 0.5f && dy < 0.5f)
                    return;
            }

            _lastPosition = new PointF(x, y);
            _recordedEvents.Add(new MouseFrameData(elapsedMs, x, y, eventType));
        }
    }

    // ==============================================================================
    // 2. ENTERPOLASYON VE PÜRÜZSÜZ OYNATMA (Linear Interpolation - Lerp)
    // ==============================================================================

    public class MouseInterpolator
    {
        private readonly IReadOnlyList<MouseFrameData> _events;

        public MouseInterpolator(IReadOnlyList<MouseFrameData> events)
        {
            _events = events;
        }

        /// <summary>
        /// Verilen video süresinde (currentMs) farenin olması gereken pürüzsüz koordinatını döndürür.
        /// İki milisaniye kaydı arasında Linear Interpolation (Lerp) uygular.
        /// </summary>
        public PointF GetInterpolatedPosition(double currentMs)
        {
            if (_events == null || _events.Count == 0)
                return PointF.Empty;

            if (currentMs <= _events[0].TimestampMs)
                return new PointF(_events[0].X, _events[0].Y);

            if (currentMs >= _events[^1].TimestampMs)
                return new PointF(_events[^1].X, _events[^1].Y);

            // İkili arama (Binary Search) ile geçerli zaman dilimini log(N) sürede bul
            int low = 0;
            int high = _events.Count - 1;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (_events[mid].TimestampMs < currentMs)
                    low = mid + 1;
                else
                    high = mid - 1;
            }

            // low elemanı currentMs'den büyük ya da eşit ilk elemandır.
            int idxB = Math.Clamp(low, 1, _events.Count - 1);
            int idxA = idxB - 1;

            var pA = _events[idxA];
            var pB = _events[idxB];

            double timeSpan = pB.TimestampMs - pA.TimestampMs;
            if (timeSpan <= 0.0001)
                return new PointF(pA.X, pA.Y);

            // Lerp faktörü t in [0.0, 1.0]
            float t = (float)((currentMs - pA.TimestampMs) / timeSpan);
            t = Math.Clamp(t, 0f, 1f);

            float lerpX = pA.X + (pB.X - pA.X) * t;
            float lerpY = pA.Y + (pB.Y - pA.Y) * t;

            return new PointF(lerpX, lerpY);
        }
    }

    // ==============================================================================
    // 3. AKILLI ZOOM VE DURUM MAKİNESİ (Smart Zoom State Machine)
    // ==============================================================================

    public enum ZoomPhase
    {
        Idle,           // Normal görünüm
        ZoomingIn,      // Büyüme animasyonu
        Holding,        // Büyütülmüş halde bekleme
        ZoomingOut      // Orijinal ekrana yumuşak dönüş
    }

    public class SmartZoomStateMachine
    {
        // Yapılandırma Parametreleri (SettingsManager üzerinden dinamik)
        public float TargetScale { get; set; } = (float)SettingsManager.Instance.MaxZoomRatio;             // Zoom çarpanı (Örn: 1.5x)
        public double ZoomInDurationMs { get; set; } = SettingsManager.Instance.ZoomSpeed * 1000.0 * 0.7;  // Zoom-In animasyon süresi
        public double ZoomOutDurationMs { get; set; } = SettingsManager.Instance.ZoomSpeed * 1000.0 * 0.85;// Zoom-Out animasyon süresi
        public double DefaultHoldDurationMs { get; set; } = SettingsManager.Instance.ZoomDuration * 1000.0;// Ekranda kalma süresi
        public SizeF VirtualBoundarySize { get; set; } = new(320, 240); // Sanal sınır kutusu boyutları

        // Durum Değişkenleri
        public ZoomPhase CurrentPhase { get; private set; } = ZoomPhase.Idle;
        public PointF ZoomCenter { get; private set; }
        public RectangleF VirtualBoundingBox { get; private set; }
        public float CurrentScale { get; private set; } = 1.0f;

        private double _phaseStartMs = 0;
        private double _holdExpiryMs = 0;
        private float _startScale = 1.0f;

        /// <summary>
        /// Tıklama olayı gerçekleştiğinde tetiklenir.
        /// </summary>
        public void TriggerClick(PointF clickPos, double currentMs)
        {
            // KURAL: ÜST ÜSTE ZOOM ENGELLEME
            // Eğer zaten Zoom aktifse ve yeni tıklama sınır kutusunun içindeyse,
            // ölçeği artırma, sadece bekleme süresini (Hold Duration) uzat!
            if (CurrentPhase == ZoomPhase.Holding || CurrentPhase == ZoomPhase.ZoomingIn)
            {
                if (VirtualBoundingBox.Contains(clickPos))
                {
                    _holdExpiryMs = Math.Max(_holdExpiryMs, currentMs + DefaultHoldDurationMs);
                    return;
                }
            }

            // Yeni Zoom Başlat
            ZoomCenter = clickPos;
            VirtualBoundingBox = new RectangleF(
                clickPos.X - VirtualBoundarySize.Width / 2f,
                clickPos.Y - VirtualBoundarySize.Height / 2f,
                VirtualBoundarySize.Width,
                VirtualBoundarySize.Height
            );

            _startScale = CurrentScale;
            _phaseStartMs = currentMs;
            _holdExpiryMs = currentMs + ZoomInDurationMs + DefaultHoldDurationMs;
            CurrentPhase = ZoomPhase.ZoomingIn;
        }

        /// <summary>
        /// Her video karesinde / render döngüsünde güncel video zamanı ve fare pozisyonu ile çağrılır.
        /// </summary>
        public void Update(double currentMs, PointF currentMousePos)
        {
            switch (CurrentPhase)
            {
                case ZoomPhase.Idle:
                    CurrentScale = 1.0f;
                    break;

                case ZoomPhase.ZoomingIn:
                    {
                        double elapsed = currentMs - _phaseStartMs;
                        float t = (float)Math.Clamp(elapsed / ZoomInDurationMs, 0.0, 1.0);

                        // Doğrusal olmayan Cubic-Ease-Out animasyonu
                        float easedT = EaseOutCubic(t);
                        CurrentScale = _startScale + (TargetScale - _startScale) * easedT;

                        // Sınır kutusu ihlali kontrolü
                        CheckBoundaryViolation(currentMousePos, currentMs);

                        if (t >= 1.0f)
                        {
                            CurrentScale = TargetScale;
                            CurrentPhase = ZoomPhase.Holding;
                        }
                    }
                    break;

                case ZoomPhase.Holding:
                    {
                        CurrentScale = TargetScale;

                        // KURAL: Sınır İhlali Kontrolü (Boundary Check)
                        // Fare sanal kutunun dışına çıktıysa bekleme süresini beklemeden yumuşakça Zoom-Out başlat
                        if (CheckBoundaryViolation(currentMousePos, currentMs))
                            break;

                        // Süre dolduysa Zoom-Out'a geç
                        if (currentMs >= _holdExpiryMs)
                        {
                            StartZoomOut(currentMs);
                        }
                    }
                    break;

                case ZoomPhase.ZoomingOut:
                    {
                        double elapsed = currentMs - _phaseStartMs;
                        float t = (float)Math.Clamp(elapsed / ZoomOutDurationMs, 0.0, 1.0);

                        // Yumuşak dönüş için Sine-Ease-Out
                        float easedT = EaseOutSine(t);
                        CurrentScale = _startScale + (1.0f - _startScale) * easedT;

                        if (t >= 1.0f)
                        {
                            CurrentScale = 1.0f;
                            CurrentPhase = ZoomPhase.Idle;
                        }
                    }
                    break;
            }
        }

        private bool CheckBoundaryViolation(PointF currentMousePos, double currentMs)
        {
            if (SettingsManager.Instance.CancelOnOutOfBounds && !VirtualBoundingBox.Contains(currentMousePos))
            {
                StartZoomOut(currentMs);
                return true;
            }
            return false;
        }

        private void StartZoomOut(double currentMs)
        {
            if (CurrentPhase == ZoomPhase.ZoomingOut || CurrentPhase == ZoomPhase.Idle)
                return;

            _startScale = CurrentScale;
            _phaseStartMs = currentMs;
            CurrentPhase = ZoomPhase.ZoomingOut;
        }

        // --- EASING FONKSİYONLARI ---

        /// <summary>
        /// Cubic-Ease-Out: 1 - (1 - t)^3
        /// </summary>
        public static float EaseOutCubic(float t)
        {
            float f = 1.0f - t;
            return 1.0f - f * f * f;
        }

        /// <summary>
        /// Sine-Ease-Out: sin(t * PI / 2)
        /// </summary>
        public static float EaseOutSine(float t)
        {
            return (float)Math.Sin(t * (Math.PI / 2.0));
        }
    }
}
