using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ScreenPowerPro.Helpers;

namespace ScreenPowerPro.Views;

/// <summary>
/// Kayıt öncesinde ekranın tam ortasında hiçbir arka plan kutusu (siyah veya koyu kart) olmadan,
/// arkasındaki masaüstünü ve açık pencereleri %100 görünür bırakarak sadece modern, minimalist
/// geri sayım yazısı çizen yerel Win32 katmanlı (layered) pencere.
/// </summary>
public class CountdownWindow
{
    private IntPtr _hwnd = IntPtr.Zero;
    private readonly int _x;
    private readonly int _y;
    private readonly int _width = 500;
    private readonly int _height = 280;

    private readonly int _totalSeconds;
    private int _remainingSeconds;
    private readonly Action _onCompleted;
    private readonly System.Timers.Timer _timer;

    #region Win32 P/Invoke Declarations

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref POINT pptDst,
        ref SIZE psize,
        IntPtr hdcSrc,
        ref POINT pptSrc,
        uint crKey,
        ref BLENDFUNCTION pblend,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BITMAPINFO pbmi,
        uint iUsage,
        out IntPtr ppvBits,
        IntPtr hSection,
        uint dwOffset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
    private static extern void CopyMemory(IntPtr dest, IntPtr src, IntPtr count);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int SW_SHOWNOACTIVATE = 4;
    private const int SW_HIDE = 0;

    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const uint ULW_ALPHA = 0x00000002;
    private const uint DIB_RGB_COLORS = 0;

    #endregion

    public CountdownWindow(int countdownSeconds, Action onCompleted)
    {
        _totalSeconds = countdownSeconds <= 0 ? 3 : countdownSeconds;
        _remainingSeconds = _totalSeconds;
        _onCompleted = onCompleted;

        int screenW = GetSystemMetrics(SM_CXSCREEN);
        int screenH = GetSystemMetrics(SM_CYSCREEN);
        _x = (screenW - _width) / 2;
        _y = (screenH - _height) / 2;

        CreateNativeLayeredWindow();

        RenderCountdown(_remainingSeconds);

        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += OnTimerTick;
        _timer.Start();
    }

    private void CreateNativeLayeredWindow()
    {
        try
        {
            int exStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            int style = WS_POPUP;

            _hwnd = CreateWindowEx(
                exStyle,
                "STATIC",
                "ScreenPowerPro_CountdownText",
                style,
                _x, _y, _width, _height,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hwnd != IntPtr.Zero)
            {
                Win32Helper.SetWindowDisplayAffinity(_hwnd, Win32Helper.WDA_EXCLUDEFROMCAPTURE);
            }
        }
        catch { }
    }

    private void RenderCountdown(int count)
    {
        if (_hwnd == IntPtr.Zero || _width <= 0 || _height <= 0) return;

        using var bmp = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            string countStr = count.ToString();

            // 1. Ekranın tam ortasında büyük, kalın ve saf beyaz geri sayım rakamı (130px)
            using var countFont = new Font("Segoe UI Variable Display", 130, FontStyle.Bold, GraphicsUnit.Pixel);

            // Hem açık hem koyu masaüstü/web sayfalarında kusursuz okunurluk için belirgin gölge
            using var shadowBrush = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            g.DrawString(countStr, countFont, shadowBrush, new RectangleF(3, 3, _width, _height - 60), sf);

            // Saf Beyaz (#FFFFFF) kalın geri sayım rakamı
            using var countBrush = new SolidBrush(Color.White);
            g.DrawString(countStr, countFont, countBrush, new RectangleF(0, 0, _width, _height - 60), sf);

            // 2. Altında sade beyaz durum metni: "Kayıt Başlıyor..." / "Recording Starting..."
            string statusText = "Kayıt Başlıyor...";
            try
            {
                var services = App.Current?.Services;
                if (services != null)
                {
                    var loc = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<ScreenPowerPro.Services.LocalizationService>(services);
                    if (loc != null)
                    {
                        statusText = loc["Countdown_Status"];
                    }
                }
            }
            catch { }

            using var subFont = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel);
            using var subShadow = new SolidBrush(Color.FromArgb(130, 0, 0, 0));
            g.DrawString(statusText, subFont, subShadow, new RectangleF(2, _height - 52, _width, 36), sf);

            using var subBrush = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
            g.DrawString(statusText, subFont, subBrush, new RectangleF(0, _height - 54, _width, 36), sf);
        }

        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        IntPtr hdcMem = CreateCompatibleDC(hdcScreen);

        try
        {
            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = _width;
            bmi.bmiHeader.biHeight = -_height; // Top-down DIB
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0; // BI_RGB

            IntPtr hBmp = CreateDIBSection(hdcMem, ref bmi, DIB_RGB_COLORS, out IntPtr ppvBits, IntPtr.Zero, 0);
            if (hBmp != IntPtr.Zero && ppvBits != IntPtr.Zero)
            {
                IntPtr hOldBmp = SelectObject(hdcMem, hBmp);

                var bmpData = bmp.LockBits(
                    new Rectangle(0, 0, _width, _height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);

                try
                {
                    int totalBytes = _width * _height * 4;
                    CopyMemory(ppvBits, bmpData.Scan0, (IntPtr)totalBytes);
                }
                finally
                {
                    bmp.UnlockBits(bmpData);
                }

                POINT ptDst = new POINT { x = _x, y = _y };
                SIZE size = new SIZE { cx = _width, cy = _height };
                POINT ptSrc = new POINT { x = 0, y = 0 };
                BLENDFUNCTION blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                UpdateLayeredWindow(_hwnd, hdcScreen, ref ptDst, ref size, hdcMem, ref ptSrc, 0, ref blend, ULW_ALPHA);

                SelectObject(hdcMem, hOldBmp);
                DeleteObject(hBmp);
            }
        }
        finally
        {
            DeleteDC(hdcMem);
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    private void OnTimerTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        _remainingSeconds--;

        if (_remainingSeconds > 0)
        {
            RenderCountdown(_remainingSeconds);
        }
        else
        {
            _timer.Stop();
            _timer.Dispose();
            Close();

            // UI iş parçacığında güvenli çalıştırma
            if (MainWindow.CurrentInstance != null)
            {
                MainWindow.CurrentInstance.DispatcherQueue.TryEnqueue(() =>
                {
                    _onCompleted?.Invoke();
                });
            }
            else
            {
                _onCompleted?.Invoke();
            }
        }
    }

    public void Activate()
    {
        if (_hwnd != IntPtr.Zero)
        {
            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        }
    }

    public void Close()
    {
        if (_hwnd != IntPtr.Zero)
        {
            ShowWindow(_hwnd, SW_HIDE);
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
