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

    private long _lastMoveTimestampMs = 0;

    public void StartTracking()
    {
        if (_isTracking) return;

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

            if (msg == Win32Helper.WM_LBUTTONDOWN)
            {
                Clicks.Add(new MouseClickEvent
                {
                    Timestamp = timestampSec,
                    Type = "left_down",
                    X = hookStruct.pt.x,
                    Y = hookStruct.pt.y
                });
            }
            else if (msg == Win32Helper.WM_LBUTTONUP)
            {
                Clicks.Add(new MouseClickEvent
                {
                    Timestamp = timestampSec,
                    Type = "left_up",
                    X = hookStruct.pt.x,
                    Y = hookStruct.pt.y
                });
            }
            else if (msg == Win32Helper.WM_RBUTTONDOWN)
            {
                Clicks.Add(new MouseClickEvent
                {
                    Timestamp = timestampSec,
                    Type = "right_down",
                    X = hookStruct.pt.x,
                    Y = hookStruct.pt.y
                });
            }
            else if (msg == Win32Helper.WM_RBUTTONUP)
            {
                Clicks.Add(new MouseClickEvent
                {
                    Timestamp = timestampSec,
                    Type = "right_up",
                    X = hookStruct.pt.x,
                    Y = hookStruct.pt.y
                });
            }
            else if (msg == Win32Helper.WM_MOUSEMOVE)
            {
                // Throttle mouse moves to at most every 25ms (40Hz) to save memory
                long nowMs = _stopwatch.ElapsedMilliseconds;
                if (nowMs - _lastMoveTimestampMs >= 25)
                {
                    _lastMoveTimestampMs = nowMs;
                    Moves.Add(new MouseMoveEvent
                    {
                        Timestamp = timestampSec,
                        X = hookStruct.pt.x,
                        Y = hookStruct.pt.y
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

    public List<ZoomEffect> GenerateAutoZoomEffects(double maxVideoDurationSec = 0)
    {
        var effects = new List<ZoomEffect>();
        double lastZoomEnd = 0;
        int zoomIndex = 1;

        foreach (var click in Clicks)
        {
            if (click.Type != "left_down") continue;

            // Avoid overlapping zooms: require at least 2.5 seconds distance
            if (click.Timestamp < lastZoomEnd + 0.5) continue;

            double startTime = Math.Max(0, click.Timestamp - 0.3);
            double duration = 2.0;

            if (maxVideoDurationSec > 0 && startTime + duration > maxVideoDurationSec)
            {
                duration = Math.Max(0.5, maxVideoDurationSec - startTime);
            }

            effects.Add(new ZoomEffect
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Name = $"Zoom {zoomIndex++}",
                StartTime = Math.Round(startTime, 2),
                Duration = Math.Round(duration, 2),
                TargetX = click.X,
                TargetY = click.Y,
                Scale = 1.5,
                Easing = "ease-in-out"
            });

            lastZoomEnd = startTime + duration;
        }

        return effects;
    }

    public void Dispose()
    {
        StopTracking();
        GC.SuppressFinalize(this);
    }
}
