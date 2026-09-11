using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenPowerPro.Helpers;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ScreenPowerPro.Views;

/// <summary>
/// Kayıt esnasında masaüstü ikonlarını ve görev çubuğunu fiziksel olarak gizlemek yerine,
/// duvar kağıdını en alt katmanda (HWND_BOTTOM) tam ekran göstererek sanal olarak maskeleyen
/// ve tıklamaları arkaya geçiren (click-through) WinUI 3 penceresi.
/// </summary>
public sealed partial class DesktopMaskWindow : Window
{
    private const uint SPI_GETDESKWALLPAPER = 0x0073;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(uint uAction, int uParam, StringBuilder lpvParam, int fuWinIni);

    private const int HWND_BOTTOM = 1;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private readonly IntPtr _hwnd;

    public DesktopMaskWindow()
    {
        InitializeComponent();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        var appWindow = AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = false;
        }

        ExtendsContentIntoTitleBar = true;

        // Tüm ekranı (görev çubuğu dahil) kaplayacak şekilde boyutlandır
        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            appWindow.MoveAndResize(new RectInt32(
                displayArea.OuterBounds.X,
                displayArea.OuterBounds.Y,
                displayArea.OuterBounds.Width,
                displayArea.OuterBounds.Height));
        }

        // Tıklama geçiren (click-through) moda al
        Win32Helper.SetWindowClickThrough(_hwnd);

        // Pencereyi en alt katmana (HWND_BOTTOM) gönder
        SendToBottom();

        _ = LoadWallpaperAsync();

        RootGrid.Loaded += (s, e) =>
        {
            SendToBottom();
        };
    }

    public void SendToBottom()
    {
        if (_hwnd != IntPtr.Zero)
        {
            SetWindowPos(_hwnd, new IntPtr(HWND_BOTTOM), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    private async Task LoadWallpaperAsync()
    {
        try
        {
            string? wallpaperPath = null;
            var sb = new StringBuilder(512);
            if (SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0) != 0)
            {
                wallpaperPath = sb.ToString().Trim();
            }

            if (string.IsNullOrEmpty(wallpaperPath) || !File.Exists(wallpaperPath))
            {
                // Windows transkript duvar kağıdı konumu
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string transcoded = Path.Combine(appData, @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (File.Exists(transcoded))
                {
                    wallpaperPath = transcoded;
                }
            }

            if (!string.IsNullOrEmpty(wallpaperPath) && File.Exists(wallpaperPath))
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(wallpaperPath);
                using IRandomAccessStream stream = await storageFile.OpenAsync(FileAccessMode.Read);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                WallpaperImage.Source = bitmap;
            }
        }
        catch
        {
            // Duvar kağıdı alınamazsa arka plan düz obsidian rengini korur
        }
    }
}
