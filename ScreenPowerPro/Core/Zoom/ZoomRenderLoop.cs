// =============================================================================
// ScreenPowerPro — ZoomRenderLoop
// Bağımsız 120 Hz fizik/render döngüsü.
//
// • System.Threading.PeriodicTimer ile tam kilitli tempo
// • Giriş (input) olaylarından tamamen bağımsız
// • Thread-safe: snapshot volatile double-buffer üzerinden okunur
// • FPS düşüşü → Step(dt) büyür ama kare başı hesaplama yoktur
// =============================================================================

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenPowerPro.Core.Zoom;

/// <summary>
/// Fizik döngüsünden gelen anlık kamera durumu.
/// Render callback'ine iletilir.
/// </summary>
public readonly struct RenderFrame
{
    public CameraSnapshot Camera    { get; init; }
    public ZoomStateV2    ZoomState { get; init; }
    public double         TimestampMs { get; init; }
}

/// <summary>
/// ZoomStateMachineV2 + SpringDamperCamera'yı sabit tempoda çalıştıran döngü.
/// Hedef tempo: 120 Hz (≈8.33 ms/kare). Ağır CPU yükünde 60 Hz'e düşer ama asla takılmaz.
/// </summary>
public sealed class ZoomRenderLoop : IDisposable
{
    // --- Konfigürasyon ---
    public int  TargetHz        { get; init; } = 120;
    public bool IsRunning       { get; private set; }

    // --- Bağımlılıklar ---
    private readonly SpringDamperCamera  _camera;
    private readonly ZoomStateMachineV2  _stateMachine;
    private readonly ZoomInputProcessor  _inputProcessor;

    // --- Thread araçları ---
    private CancellationTokenSource? _cts;
    private Task?                    _loopTask;
    private readonly Stopwatch       _stopwatch = new();

    // --- Render callback ---
    /// <summary>
    /// Her fizik tick'inde UI thread'inde (DispatcherQueue üzerinden) çağrılır.
    /// </summary>
    public Action<RenderFrame>? OnFrame { get; set; }

    /// <summary>
    /// DispatcherQueue.TryEnqueue'e alternatif: döngü bu action ile UI'ya iletir.
    /// WinUI3'te: frame => DispatcherQueue.TryEnqueue(() => OnFrame(frame))
    /// </summary>
    public Action<Action>? DispatchToUI { get; set; }

    public ZoomRenderLoop(
        SpringDamperCamera  camera,
        ZoomStateMachineV2  stateMachine,
        ZoomInputProcessor  inputProcessor)
    {
        _camera         = camera;
        _stateMachine   = stateMachine;
        _inputProcessor = inputProcessor;
    }

    /// <summary>
    /// Döngüyü başlatır.
    /// </summary>
    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _cts      = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Döngüyü durdurur ve kaynakları serbest bırakır.
    /// </summary>
    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts?.Cancel();
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        TimeSpan interval = TimeSpan.FromSeconds(1.0 / TargetHz);
        _stopwatch.Restart();
        double lastMs = 0;

        using var timer = new PeriodicTimer(interval);

        try
        {
            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
            {
                double nowMs = _stopwatch.Elapsed.TotalMilliseconds;
                double dt    = (nowMs - lastMs) / 1000.0; // saniye
                lastMs       = nowMs;

                // Fizik adımı
                _camera.Step(dt);

                // State machine güncelleme
                _stateMachine.Update(nowMs);

                // Render frame yayımı — UI thread'e taşı
                if (OnFrame != null)
                {
                    var frame = new RenderFrame
                    {
                        Camera      = _camera.Snapshot,
                        ZoomState   = _stateMachine.CurrentState,
                        TimestampMs = nowMs
                    };

                    if (DispatchToUI != null)
                        DispatchToUI(() => OnFrame(frame));
                    else
                        OnFrame(frame); // Döngü zaten UI thread'indeyse doğrudan çağır
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal duruş
        }
    }

    // --- Thread-safe giriş yönlendirme ---
    // Bu metodlar herhangi bir thread'den (hook callback dahil) güvenle çağrılabilir.

    public void FeedClick(float x, float y, double timestampMs)
    {
        var input = _inputProcessor.ProcessClick(x, y, timestampMs);
        _stateMachine.OnInput(input);
    }

    public void FeedMove(float x, float y, double timestampMs)
    {
        var input = _inputProcessor.ProcessMove(x, y, timestampMs);
        _stateMachine.OnInput(input);
    }

    public void FeedFocusLost(double timestampMs)
    {
        var input = _inputProcessor.FocusLost(timestampMs);
        _stateMachine.OnInput(input);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}

/// <summary>
/// ZoomRenderLoop için factory — SettingsManager üzerinden konfigüre eder.
/// </summary>
public static class ZoomRenderLoopFactory
{
    public static (ZoomRenderLoop Loop, SpringDamperCamera Camera,
                   ZoomStateMachineV2 StateMachine, ZoomInputProcessor Input)
        Create(Services.SettingsManager settings,
               double videoWidth  = 1920,
               double videoHeight = 1080)
    {
        var camera    = new SpringDamperCamera();
        var stateMach = new ZoomStateMachineV2(camera)
        {
            TargetScale    = settings.MaxZoomRatio,
            HoldDurationMs = settings.ZoomDuration * 1000.0,
            AnticipationMs = settings.PreClickAnticipationMs,
            VideoWidth     = videoWidth,
            VideoHeight    = videoHeight,
            EasingName     = settings.ZoomEasingFunction,
        };
        var input = new ZoomInputProcessor
        {
            SpatialThresholdPx     = 80.0,
            TemporalDebounceMs     = 80.0,
            FastMoveThresholdPxPerMs = 3.0
        };
        var loop = new ZoomRenderLoop(camera, stateMach, input)
        {
            TargetHz = 120
        };

        return (loop, camera, stateMach, input);
    }
}
