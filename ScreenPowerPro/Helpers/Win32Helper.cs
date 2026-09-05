using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenPowerPro.Helpers;

public static class Win32Helper
{
    // Window Display Affinity constants
    public const uint WDA_NONE = 0x00000000;
    public const uint WDA_MONITOR = 0x00000001;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    // ShowWindow constants
    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

    // Hooks
    public const int WH_MOUSE_LL = 14;
    public const int WH_KEYBOARD_LL = 13;

    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MOUSEMOVE = 0x0200;

    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    // Desktop icons toggle
    public static void SetDesktopIconsVisible(bool visible)
    {
        try
        {
            IntPtr hProgman = FindWindow("Progman", null);
            IntPtr hShellView = FindWindowEx(hProgman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (hShellView == IntPtr.Zero)
            {
                // In Win10/11 SHELLDLL_DefView may be a child of WorkerW
                IntPtr hWorkerW = IntPtr.Zero;
                do
                {
                    hWorkerW = FindWindowEx(IntPtr.Zero, hWorkerW, "WorkerW", null);
                    hShellView = FindWindowEx(hWorkerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                } while (hShellView == IntPtr.Zero && hWorkerW != IntPtr.Zero);
            }

            if (hShellView != IntPtr.Zero)
            {
                ShowWindow(hShellView, visible ? SW_SHOW : SW_HIDE);
            }
        }
        catch { }
    }

    // Taskbar toggle
    public static void SetTaskbarVisible(bool visible)
    {
        try
        {
            IntPtr hTaskbar = FindWindow("Shell_TrayWnd", null);
            if (hTaskbar != IntPtr.Zero)
            {
                ShowWindow(hTaskbar, visible ? SW_SHOW : SW_HIDE);
            }
        }
        catch { }
    }

    // Window Enumeration
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    public record WindowInfo(IntPtr Handle, string Title, int Width, int Height);

    public static List<WindowInfo> GetCapturableWindows()
    {
        var list = new List<WindowInfo>();
        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            string title = sb.ToString().Trim();

            if (string.IsNullOrEmpty(title)) return true;
            if (title == "Program Manager" || title == "ScreenPowerPro") return true;

            GetWindowRect(hWnd, out RECT rect);
            if (rect.Width > 100 && rect.Height > 100)
            {
                list.Add(new WindowInfo(hWnd, title, rect.Width, rect.Height));
            }
            return true;
        }, IntPtr.Zero);

        return list;
    }
}
