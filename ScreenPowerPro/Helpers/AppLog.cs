using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ScreenPowerPro.Helpers;

/// <summary>
/// Uygulama çalışma zamanındaki olayları, navigasyonları, kayıt işlemlerini
/// ve hataları gerçek zamanlı olarak hem özel açılan Windows Konsoluna (AllocConsole)
/// hem de diskteki log dosyasına yazan merkezi loglayıcı.
/// </summary>
public static class AppLog
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    private static readonly object _lock = new();
    private static readonly string _logFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "app_events.log"
    );

    public static void InitializeConsole()
    {
        try
        {
            if (GetConsoleWindow() == IntPtr.Zero)
            {
                AllocConsole();
            }

            Console.Title = "ScreenPowerPro :: Canlı Olay & Hata Günlüğü (Debug Console)";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("================================================================================");
            Console.WriteLine(" ScreenPowerPro Gerçek Zamanlı Debug Konsolu Başlatıldı");
            Console.WriteLine($" Zaman: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Log Dosyası: {_logFilePath}");
            Console.WriteLine("================================================================================\n");
            Console.ResetColor();

            Info("Debug konsolu ve dosya loglayıcı hazır.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppLog.InitializeConsole] Hata: {ex.Message}");
        }
    }

    public static void Info(string message) => Write("INFO", message, ConsoleColor.Cyan);
    public static void Success(string message) => Write("SUCCESS", message, ConsoleColor.Green);
    public static void Warn(string message) => Write("WARN", message, ConsoleColor.Yellow);
    public static void Error(string message, Exception? ex = null)
    {
        string fullMsg = ex != null ? $"{message}\n[HATA DETAYI] {ex.GetType().FullName}: {ex.Message}\n[STACK TRACE]\n{ex.StackTrace}" : message;
        Write("ERROR", fullMsg, ConsoleColor.Red);
    }
    public static void Event(string category, string message) => Write(category.ToUpperInvariant(), message, ConsoleColor.Magenta);

    private static void Write(string tag, string message, ConsoleColor color)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string formatted = $"[{timestamp}] [{tag}] {message}";

        lock (_lock)
        {
            // Konsola renkli yaz
            var oldColor = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine(formatted);
            }
            finally
            {
                Console.ForegroundColor = oldColor;
            }

            // Dosyaya yaz
            try
            {
                File.AppendAllText(_logFilePath, formatted + Environment.NewLine);
                // Kullanıcının ana klasörüne de ek kopyasını bırak
                string rootLog = @"C:\Users\burak\ScreenPowerPro\app_events.log";
                File.AppendAllText(rootLog, formatted + Environment.NewLine);
            }
            catch { }

            System.Diagnostics.Debug.WriteLine(formatted);
        }
    }
}
