using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ScreenPowerPro.Helpers;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Services;

public class InputTrackerService : IDisposable
{
    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private Win32Helper.LowLevelProc? _mouseProc;
    private Win32Helper.LowLevelProc? _keyboardProc;

    private Stopwatch? _stopwatch;
    private bool _isTracking;

    public List<MouseClickEvent> Clicks { get; private set; } = new();
    public List<MouseMoveEvent> Moves { get; private set; } = new();
    public List<KeystrokeEvent> Keystrokes { get; private set; } = new();

    private int _originX = 0;
    private int _originY = 0;
    private long _lastMoveTimestampMs = 0;

    public void StartTracking(int originX = 0, int originY = 0)
    {
        if (_isTracking) return;

        _originX = originX;
        _originY = originY;

        Clicks.Clear();
        Moves.Clear();
        Keystrokes.Clear();

        _stopwatch = Stopwatch.StartNew();
        _isTracking = true;

        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        IntPtr modHandle = Win32Helper.GetModuleHandle(curModule?.ModuleName);

        _mouseHook = Win32Helper.SetWindowsHookEx(Win32Helper.WH_MOUSE_LL, _mouseProc, modHandle, 0);
        _keyboardHook = Win32Helper.SetWindowsHookEx(Win32Helper.WH_KEYBOARD_LL, _keyboardProc, modHandle, 0);
    }

    public void StopTracking()
    {
        if (!_isTracking) return;

        _isTracking = false;
        _stopwatch?.Stop();

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
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isTracking && _stopwatch != null)
        {
            var hookStruct = Marshal.PtrToStructure<Win32Helper.MSLLHOOKSTRUCT>(lParam);
            double timestampSec = _stopwatch.ElapsedMilliseconds / 1000.0;
            int msg = wParam.ToInt32();

            // Ekran koordinatını kayıt bölgesi koordinatına dönüştür
            int relX = hookStruct.pt.x - _originX;
            int relY = hookStruct.pt.y - _originY;

            if (msg == Win32Helper.WM_LBUTTONDOWN)
            {
                Clicks.Add(new MouseClickEvent
                {
                    Timestamp = timestampSec,
                    Type = "left_down",
                    X = relX,
                    Y = relY
                });
            }
            else if (msg == Win32Helper.WM_LBUTTONUP)
            {
                Clicks.Add(new MouseClickEvent
                {
                    Timestamp = timestampSec,
                    Type = "left_up",
                    X = relX,
                    Y = relY
                });
            }
            else if (msg == Win32Helper.WM_RBUTTONDOWN)
            {
                Clicks.Add(new MouseClickEvent
                {
                    Timestamp = timestampSec,
                    Type = "right_down",
                    X = relX,
                    Y = relY
                });
            }
            else if (msg == Win32Helper.WM_RBUTTONUP)
            {
                Clicks.Add(new MouseClickEvent
                {
                    Timestamp = timestampSec,
                    Type = "right_up",
                    X = relX,
                    Y = relY
                });
            }
            else if (msg == Win32Helper.WM_MOUSEMOVE)
            {
                // Bellek ve performans için fare hareketlerini en fazla 25ms'de bir (40Hz) örnekle
                long nowMs = _stopwatch.ElapsedMilliseconds;
                if (nowMs - _lastMoveTimestampMs >= 25)
                {
                    _lastMoveTimestampMs = nowMs;
                    Moves.Add(new MouseMoveEvent
                    {
                        Timestamp = timestampSec,
                        X = relX,
                        Y = relY
                    });
                }
            }
        }

        return Win32Helper.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isTracking && _stopwatch != null)
        {
            int msg = wParam.ToInt32();
            if (msg == Win32Helper.WM_KEYDOWN || msg == Win32Helper.WM_SYSKEYDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<Win32Helper.KBDLLHOOKSTRUCT>(lParam);
                double timestampSec = _stopwatch.ElapsedMilliseconds / 1000.0;
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
        double defaultScale = 1.5)
    {
        var zoomEngine = new ZoomEngineService();
        return zoomEngine.GenerateZoomEffectsFromClicks(
            Clicks,
            autoZoomMode: autoZoomMode,
            defaultScale: defaultScale,
            maxVideoDurationSec: maxVideoDurationSec);
    }

    public void Dispose()
    {
        StopTracking();
        GC.SuppressFinalize(this);
    }
}
