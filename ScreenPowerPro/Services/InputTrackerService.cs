using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ScreenPowerPro.Core.Tracking;
using ScreenPowerPro.Helpers;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Services;

public class InputTrackerService : IDisposable
{
    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private Win32Helper.LowLevelProc? _mouseProc;
    private Win32Helper.LowLevelProc? _keyboardProc;

    private readonly PreciseMouseRecorder _mouseRecorder = new();
    private bool _isHookInstalled;

    public PreciseMouseRecorder MouseRecorder => _mouseRecorder;
    public RecordingState State => _mouseRecorder.State;
    public IReadOnlyList<MouseFrameData> RecordedFrames => _mouseRecorder.RecordedEvents;

    public List<MouseClickEvent> Clicks { get; private set; } = new();
    public List<MouseMoveEvent> Moves { get; private set; } = new();
    public List<KeystrokeEvent> Keystrokes { get; private set; } = new();

    private int _originX = 0;
    private int _originY = 0;
    private long _lastMoveTimestampMs = 0;
    private int _lastRecordedX = -9999;
    private int _lastRecordedY = -9999;

    /// <summary>
    /// Kayıt hazırlık aşaması: Hook'ları kurar ancak State'i Preparing yapar.
    /// Kayıt öncesi (countdown / alan seçimi) esnasında fare hareketlerinin kayda sızmasını engeller.
    /// </summary>
    public void Prepare(int originX = 0, int originY = 0)
    {
        _originX = originX;
        _originY = originY;

        Clicks.Clear();
        Moves.Clear();
        Keystrokes.Clear();

        _lastMoveTimestampMs = 0;
        _lastRecordedX = -9999;
        _lastRecordedY = -9999;

        _mouseRecorder.Prepare();
        InstallHooks();
    }

    /// <summary>
    /// Video kaydı tam olarak başladığında (FFmpeg ilk kareleri yakalamaya başladığında) çağrılır.
    /// Kayıt öncesi sızan verileri kesin olarak temizler, milisaniye sayacını sıfırdan başlatır
    /// ve State'i Recording yapar.
    /// </summary>
    public void StartRecording()
    {
        Clicks.Clear();
        Moves.Clear();
        Keystrokes.Clear();

        _lastMoveTimestampMs = 0;
        _lastRecordedX = -9999;
        _lastRecordedY = -9999;

        InstallHooks();
        _mouseRecorder.StartRecording();
    }

    /// <summary>
    /// Eski metodlarla uyumluluk için: Prepare ve derhal StartRecording yapar.
    /// </summary>
    public void StartTracking(int originX = 0, int originY = 0)
    {
        Prepare(originX, originY);
        StartRecording();
    }

    /// <summary>
    /// FFmpeg veya kayıt başladığında tam 0.0 milisaniye anına senkronize eder.
    /// </summary>
    public void SyncRecordingStart()
    {
        StartRecording();
    }

    public void PauseTracking()
    {
        _mouseRecorder.PauseRecording();
    }

    public void ResumeTracking()
    {
        _mouseRecorder.ResumeRecording();
    }

    public void StopTracking()
    {
        _mouseRecorder.StopRecording();
        UninstallHooks();
    }

    private void InstallHooks()
    {
        if (_isHookInstalled) return;

        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        IntPtr modHandle = Win32Helper.GetModuleHandle(curModule?.ModuleName);

        _mouseHook = Win32Helper.SetWindowsHookEx(Win32Helper.WH_MOUSE_LL, _mouseProc, modHandle, 0);
        _keyboardHook = Win32Helper.SetWindowsHookEx(Win32Helper.WH_KEYBOARD_LL, _keyboardProc, modHandle, 0);

        _isHookInstalled = true;
    }

    private void UninstallHooks()
    {
        if (!_isHookInstalled) return;

        if (_mouseHook != IntPtr.Zero)
        {
            Win32Helper.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            Win32Helper.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        _isHookInstalled = false;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _mouseRecorder.State == RecordingState.Recording)
        {
            var hookStruct = Marshal.PtrToStructure<Win32Helper.MSLLHOOKSTRUCT>(lParam);
            double elapsedMs = _mouseRecorder.ElapsedMilliseconds;
            double timestampSec = Math.Round(elapsedMs / 1000.0, 4);
            int msg = wParam.ToInt32();

            // Ekran koordinatını kayıt bölgesi koordinatına dönüştür
            int relX = hookStruct.pt.x - _originX;
            int relY = hookStruct.pt.y - _originY;

            MouseEventType? evtType = null;
            string? clickType = null;

            if (msg == Win32Helper.WM_LBUTTONDOWN)
            {
                evtType = MouseEventType.LeftDown;
                clickType = "left_down";
            }
            else if (msg == Win32Helper.WM_LBUTTONUP)
            {
                evtType = MouseEventType.LeftUp;
                clickType = "left_up";
            }
            else if (msg == Win32Helper.WM_RBUTTONDOWN)
            {
                evtType = MouseEventType.RightDown;
                clickType = "right_down";
            }
            else if (msg == Win32Helper.WM_RBUTTONUP)
            {
                evtType = MouseEventType.RightUp;
                clickType = "right_up";
            }
            else if (msg == Win32Helper.WM_MOUSEMOVE)
            {
                evtType = MouseEventType.Move;
            }

            if (evtType.HasValue)
            {
                int prevCount = _mouseRecorder.RecordedEvents.Count;
                _mouseRecorder.OnRawMouseInput(relX, relY, evtType.Value);

                // Eğer yeni bir olay kaydedildiyse (titreşim filtresini geçtiyse)
                if (_mouseRecorder.RecordedEvents.Count > prevCount)
                {
                    if (clickType != null)
                    {
                        _lastRecordedX = relX;
                        _lastRecordedY = relY;
                        Clicks.Add(new MouseClickEvent
                        {
                            Timestamp = timestampSec,
                            Type = clickType,
                            X = relX,
                            Y = relY
                        });
                    }
                    else if (evtType == MouseEventType.Move)
                    {
                        // 60-100Hz frekans kısıtlaması
                        long nowMs = (long)elapsedMs;
                        if (nowMs - _lastMoveTimestampMs >= 10 && (relX != _lastRecordedX || relY != _lastRecordedY))
                        {
                            _lastMoveTimestampMs = nowMs;
                            _lastRecordedX = relX;
                            _lastRecordedY = relY;
                            Moves.Add(new MouseMoveEvent
                            {
                                Timestamp = timestampSec,
                                X = relX,
                                Y = relY
                            });
                        }
                    }
                }
            }
        }

        return Win32Helper.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _mouseRecorder.State == RecordingState.Recording)
        {
            int msg = wParam.ToInt32();
            if (msg == Win32Helper.WM_KEYDOWN || msg == Win32Helper.WM_SYSKEYDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<Win32Helper.KBDLLHOOKSTRUCT>(lParam);
                double elapsedMs = _mouseRecorder.ElapsedMilliseconds;
                double timestampSec = Math.Round(elapsedMs / 1000.0, 4);
                string keyName = ((Windows.System.VirtualKey)hookStruct.vkCode).ToString();

                var modifiers = new List<string>();
                if ((hookStruct.flags & 0x20) != 0) // Alt key
                    modifiers.Add("Alt");

                Keystrokes.Add(new KeystrokeEvent
                {
                    Timestamp = timestampSec,
                    Key = keyName,
                    Modifiers = modifiers
                });
            }
        }

        return Win32Helper.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Kayıt esnasında toplanan fare tıklamalarından akıllı zoom efektleri üretir.
    /// ZoomEngineService kullanarak kümeleme (clustering) ve yumuşak geçiş hesaplamalarını uygular.
    /// </summary>
    public List<ZoomEffect> GenerateAutoZoomEffects(
        double maxVideoDurationSec = 0,
        string autoZoomMode = "smooth",
        double defaultScale = 1.5,
        double videoWidth = 1920,
        double videoHeight = 1080)
    {
        var zoomEngine = new ZoomEngineService();
        return zoomEngine.GenerateZoomEffectsFromClicks(
            Clicks,
            moves: Moves,
            autoZoomMode: autoZoomMode,
            defaultScale: defaultScale,
            maxVideoDurationSec: maxVideoDurationSec,
            defaultCenterX: (videoWidth > 0 ? videoWidth : 1920) / 2.0,
            defaultCenterY: (videoHeight > 0 ? videoHeight : 1080) / 2.0);
    }

    public void Dispose()
    {
        StopTracking();
        GC.SuppressFinalize(this);
    }
}
