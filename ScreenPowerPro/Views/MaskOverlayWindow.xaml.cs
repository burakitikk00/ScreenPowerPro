using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ScreenPowerPro.Helpers;
using Windows.Graphics;

namespace ScreenPowerPro.Views;

/// <summary>
/// Özel bölge (Custom Region) kaydı sırasında seçili alanın dışını karartan,
/// seçili bölgeyi ise saydam bırakarak kullanıcının odaklanmasını sağlayan tam ekran maske penceresi.
/// </summary>
public sealed partial class MaskOverlayWindow : Window
{
    public MaskOverlayWindow(int regionX, int regionY, int regionWidth, int regionHeight)
    {
        InitializeComponent();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        try
        {
            var services = App.Current?.Services;
            if (services != null)
            {
                var loc = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<ScreenPowerPro.Services.LocalizationService>(services);
                if (loc != null)
                {
                    Title = loc.CurrentLanguage == "en" ? "Region Mask" : "Bölge Maskesi";
                }
            }
        }
        catch { }

        // Kayıt videosunda bu karartmanın görünmemesi için dışla
        Win32Helper.SetWindowDisplayAffinity(hwnd, Win32Helper.WDA_EXCLUDEFROMCAPTURE);

        // Kullanıcının altındaki uygulamaları normal kullanabilmesi için tıklama geçiren yap
        Win32Helper.SetWindowClickThrough(hwnd);

        // Tam ekran yap
        var appWindow = AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.Maximize();
        }

        // Ekran boyutunu al ve maske parçalarını konumlandır
        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        int screenWidth = displayArea?.WorkArea.Width ?? 1920;
        int screenHeight = displayArea?.WorkArea.Height ?? 1080;

        UpdateCutout(regionX, regionY, regionWidth, regionHeight, screenWidth, screenHeight);
    }

    private void UpdateCutout(int x, int y, int width, int height, int screenW, int screenH)
    {
        // Üst panel
        MaskTop.Height = Math.Max(0, y);

        // Alt panel
        int bottomHeight = Math.Max(0, screenH - (y + height));
        MaskBottom.Height = bottomHeight;

        // Sol panel
        MaskLeft.Margin = new Thickness(0, y, 0, bottomHeight);
        MaskLeft.Width = Math.Max(0, x);

        // Sağ panel
        int rightWidth = Math.Max(0, screenW - (x + width));
        MaskRight.Margin = new Thickness(0, y, 0, bottomHeight);
        MaskRight.Width = rightWidth;

        // Seçim çerçevesi
        CutoutBorder.Margin = new Thickness(x, y, 0, 0);
        CutoutBorder.Width = width;
        CutoutBorder.Height = height;
    }
}
