using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ScreenPowerPro.Helpers;

public static class SystemHelper
{
    private const string AutoStartRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ScreenPowerPro";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    /// <summary>
    /// Algılanan birincil ekran kartının (GPU) adını döndürür.
    /// </summary>
    public static string GetGraphicsCardName()
    {
        try
        {
            var device = new DISPLAY_DEVICE();
            device.cb = Marshal.SizeOf(device);

            for (uint id = 0; EnumDisplayDevices(null, id, ref device, 0); id++)
            {
                if ((device.StateFlags & 0x00000001) != 0) // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
                {
                    if (!string.IsNullOrWhiteSpace(device.DeviceString))
                    {
                        return device.DeviceString.Trim();
                    }
                }
            }

            // Yedek kontrol: İlk cihazın adını al
            device = new DISPLAY_DEVICE();
            device.cb = Marshal.SizeOf(device);
            if (EnumDisplayDevices(null, 0, ref device, 0) && !string.IsNullOrWhiteSpace(device.DeviceString))
            {
                return device.DeviceString.Trim();
            }
        }
        catch { }

        return "NVIDIA GeForce / Intel / AMD";
    }

    /// <summary>
    /// Windows başlangıcında otomatik başlama durumunu kontrol eder.
    /// </summary>
    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Windows başlangıcında otomatik başlama kaydını açar veya kapatır.
    /// </summary>
    public static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, true);
            if (key == null) return;

            if (enable)
            {
                string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ScreenPowerPro.exe");
                key.SetValue(AppName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch { }
    }
}
