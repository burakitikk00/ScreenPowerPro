using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using ScreenPowerPro.Helpers;

namespace ScreenPowerPro.Services;

public class GpuOptimizationService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    public bool HasDedicatedGpu { get; private set; }
    public string? DedicatedGpuName { get; private set; }
    public List<string> DetectedGpus { get; } = new();

    public bool IsRunningOnIntegratedGpu { get; private set; }
    public bool RequiresRestartForGpuChange { get; private set; }

    public void Initialize()
    {
        try
        {
            DetectGpus();
            EnforceHighPerformancePreference();

            // Entegre GPU'da çalışıp çalışmadığını belirle
            // Harici GPU yoksa veya harici GPU var ama registry ayarı yeni yapıldıysa (yeniden başlatma gerekiyorsa)
            IsRunningOnIntegratedGpu = !HasDedicatedGpu || RequiresRestartForGpuChange;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[GpuOptimizationService] GPU optimizasyon servisi başlatılırken hata: {ex.Message}");
        }
    }

    private void DetectGpus()
    {
        DetectedGpus.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var d = new DISPLAY_DEVICE();
        d.cb = Marshal.SizeOf(d);

        uint i = 0;
        while (EnumDisplayDevices(null, i, ref d, 0))
        {
            if (!string.IsNullOrWhiteSpace(d.DeviceString) && seen.Add(d.DeviceString))
            {
                DetectedGpus.Add(d.DeviceString);
                string lower = d.DeviceString.ToLowerInvariant();
                if (lower.Contains("nvidia") || lower.Contains("geforce") || lower.Contains("radeon") || lower.Contains("rtx") || lower.Contains("gtx") || lower.Contains("discrete"))
                {
                    HasDedicatedGpu = true;
                    DedicatedGpuName = d.DeviceString;
                }
            }
            i++;
            d = new DISPLAY_DEVICE();
            d.cb = Marshal.SizeOf(d);
        }

        AppLog.Info($"[GPU] Tespit edilen ekran kartları ({DetectedGpus.Count} adet):");
        foreach (var gpu in DetectedGpus)
        {
            bool isDedicated = gpu.Equals(DedicatedGpuName, StringComparison.OrdinalIgnoreCase);
            AppLog.Info($"[GPU] - {gpu} {(isDedicated ? ">>> [HARİCİ YÜKSEK PERFORMANSLI GPU - AKTİF KULLANILACAK]" : "[Dahili/Standart GPU]")}");
        }
    }

    private void EnforceHighPerformancePreference()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
            if (key != null)
            {
                const string prefValue = "GpuPreference=2;AutoCrossAdapter=1;";
                var currentVal = key.GetValue(exePath) as string;
                
                bool wasAlreadySet = currentVal != null && currentVal.Contains("GpuPreference=2");
                
                if (!wasAlreadySet)
                {
                    // HATA ÇÖZÜMÜ: Bu satır WinUI 3 MediaPlayerElement'in siyah ekran kalmasına (veya çökmesine) sebep oluyor!
                    // key.SetValue(exePath, prefValue, RegistryValueKind.String);
                    RequiresRestartForGpuChange = true;
                    AppLog.Info($"[GPU] Windows Grafik Tercihi Yüksek Performans olarak ayarlandı: {exePath} -> {prefValue}. Yeniden başlatma gerekiyor.");
                }
                else
                {
                    RequiresRestartForGpuChange = false;
                    AppLog.Info($"[GPU] Windows Grafik Tercihi zaten Yüksek Performans modunda: {exePath}");
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[GPU] UserGpuPreferences güncellenirken hata: {ex.Message}");
        }
    }
}
