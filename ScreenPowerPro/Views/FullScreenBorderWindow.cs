using System;
using System.Runtime.InteropServices;
using ScreenPowerPro.Helpers;
using Windows.Graphics;

namespace ScreenPowerPro.Views;

/// <summary>
/// Ekran veya özel bölge kaydı alanını kesikli mavi çizgilerle gösteren,
/// arka planı %100 şeffaf (desktop ve pencereleri gösteren), tıklama geçiren (click-through)
/// yüksek performanslı yerel Win32 katmanlı (layered) çerçeve penceresi.
/// </summary>
public class FullScreenBorderWindow
{
    public static FullScreenBorderWindow? Instance { get; private set; }

    private IntPtr _hwnd = IntPtr.Zero;
    private int _x;
    private int _y;
    private int _width;
    private int _height;

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

    public FullScreenBorderWindow(RectInt32? bounds = null)
    {
        // Önceki aktif örnek varsa kapat
        Instance?.Close();
        Instance = this;

        if (bounds.HasValue)
        {
            _x = bounds.Value.X;
            _y = bounds.Value.Y;
            _width = bounds.Value.Width;
            _height = bounds.Value.Height;
        }
        else
        {
            _x = 0;
            _y = 0;
            _width = GetSystemMetrics(SM_CXSCREEN);
            _height = GetSystemMetrics(SM_CYSCREEN);
        }

        CreateNativeLayeredWindow();
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
                "ScreenPowerPro_BorderOverlay",
                style,
                _x, _y, _width, _height,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hwnd != IntPtr.Zero)
            {
                // Video kaydında çerçevenin görünmesini engelle
                Win32Helper.SetWindowDisplayAffinity(_hwnd, Win32Helper.WDA_EXCLUDEFROMCAPTURE);
                RenderDashedBorder();
            }
        }
        catch { }
    }

    private void RenderDashedBorder()
    {
        if (_hwnd == IntPtr.Zero || _width <= 0 || _height <= 0) return;

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

                int totalBytes = _width * _height * 4;
                byte[] pixels = new byte[totalBytes]; // Varsayılan olarak tüm pikseller 0 alpha (tamamen saydam)

                // Kesikli çizgi parametreleri: Canlı mavi/cyan (#38BDF8)
                // BGRA: B = 248, G = 189, R = 56, A = 255
                const byte b = 248;
                const byte g = 189;
                const byte r = 56;
                const byte a = 255;
                const int thickness = 4;
                const int dashLength = 12;
                const int gapLength = 8;
                const int period = dashLength + gapLength;

                // Üst ve Alt kenarlar
                for (int px = 0; px < _width; px++)
                {
                    if ((px % period) < dashLength)
                    {
                        for (int t = 0; t < thickness; t++)
                        {
                            // Üst kenar
                            int topOffset = (t * _width + px) * 4;
                            if (topOffset + 3 < totalBytes)
                            {
                                pixels[topOffset] = b;
                                pixels[topOffset + 1] = g;
                                pixels[topOffset + 2] = r;
                                pixels[topOffset + 3] = a;
                            }

                            // Alt kenar
                            int botY = _height - 1 - t;
                            int botOffset = (botY * _width + px) * 4;
                            if (botOffset + 3 < totalBytes)
                            {
                                pixels[botOffset] = b;
                                pixels[botOffset + 1] = g;
                                pixels[botOffset + 2] = r;
                                pixels[botOffset + 3] = a;
                            }
                        }
                    }
                }

                // Sol ve Sağ kenarlar
                for (int py = 0; py < _height; py++)
                {
                    if ((py % period) < dashLength)
                    {
                        for (int t = 0; t < thickness; t++)
                        {
                            // Sol kenar
                            int leftOffset = (py * _width + t) * 4;
                            if (leftOffset + 3 < totalBytes)
                            {
                                pixels[leftOffset] = b;
                                pixels[leftOffset + 1] = g;
                                pixels[leftOffset + 2] = r;
                                pixels[leftOffset + 3] = a;
                            }

                            // Sağ kenar
                            int rightX = _width - 1 - t;
                            int rightOffset = (py * _width + rightX) * 4;
                            if (rightOffset + 3 < totalBytes)
                            {
                                pixels[rightOffset] = b;
                                pixels[rightOffset + 1] = g;
                                pixels[rightOffset + 2] = r;
                                pixels[rightOffset + 3] = a;
                            }
                        }
                    }
                }

                Marshal.Copy(pixels, 0, ppvBits, totalBytes);

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

    public void Activate()
    {
        if (_hwnd != IntPtr.Zero)
        {
            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        }
    }

    public void UpdateBounds(RectInt32 bounds)
    {
        _x = bounds.X;
        _y = bounds.Y;
        _width = bounds.Width;
        _height = bounds.Height;
        RenderDashedBorder();
    }

    public void Close()
    {
        if (_hwnd != IntPtr.Zero)
        {
            ShowWindow(_hwnd, SW_HIDE);
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }
}
