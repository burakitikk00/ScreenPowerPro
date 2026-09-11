using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenPowerPro.Helpers;
using ScreenPowerPro.Views;
using Windows.Graphics;

namespace ScreenPowerPro;

/// <summary>
/// ScreenPowerPro ana uygulama penceresi. Başlık çubuğu özelleştirmelerini,
/// sayfalar arası navigasyonu (Dashboard, Library, Editor, Export) ve
/// pencere boyutu yönetimini koordine eder.
/// </summary>
public sealed partial class MainWindow : Window
{
    public static MainWindow? CurrentInstance { get; private set; }

    public MainWindow()
    {
        CurrentInstance = this;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Özel başlık çubuğu renkleri
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(25, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(50, 255, 255, 255);
            titleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
        }

        // Başlangıç pencere boyutu (1160 x 310) - Kompakt ve sabit boyutlu
        ResizeForDashboard();

        // İlk sayfa olarak Dashboard'a yönlendir
        AppLog.Event("NAV", "İlk açılış: Dashboard'a yönlendiriliyor.");
        RootFrame.Navigate(typeof(DashboardPage));
    }

    public IntPtr GetWindowHandle()
    {
        return WinRT.Interop.WindowNative.GetWindowHandle(this);
    }

    public void ResizeAndCenter(int width, int height)
    {
        AppWindow.Resize(new SizeInt32(width, height));
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            int x = (displayArea.WorkArea.Width - width) / 2;
            int y = (displayArea.WorkArea.Height - height) / 2;
            AppWindow.Move(new PointInt32(Math.Max(0, x), Math.Max(0, y)));
        }
    }

    /// <summary>
    /// Dashboard moduna göre pencereyi optimize eder. Boyutlandırmayı kapatır ve sabit tutar.
    /// </summary>
    public void ResizeForDashboard()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }
        ResizeAndCenter(1160, 315);
    }

    /// <summary>
    /// Video düzenleyici modu için geniş çalışma alanına geçer.
    /// </summary>
    public void ResizeForEditor()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Restore();
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }
        ResizeAndCenter(1280, 850);
    }

    public void NavigateToDashboard()
    {
        AppLog.Event("NAV", "DashboardPage sayfasına geçiliyor.");
        ResizeForDashboard();
        RootFrame.Navigate(typeof(DashboardPage));
    }

    public void NavigateToLibrary()
    {
        AppLog.Event("NAV", "LibraryPage sayfasına geçiliyor.");
        RootFrame.Navigate(typeof(LibraryPage));
    }

    public void NavigateToEditor(string projectDir)
    {
        AppLog.Event("NAV", $"NavigateToEditor çağrıldı. Hedef proje: '{projectDir}'");
        try
        {
            ResizeForEditor();
            bool result = RootFrame.Navigate(typeof(EditorPage), projectDir);
            AppLog.Success($"EditorPage navigasyonu tamamlandı (Result: {result}).");
        }
        catch (Exception ex)
        {
            AppLog.Error($"[MainWindow] EditorPage sayfasına navigasyon sırasında KRİTİK HATA!", ex);
        }
    }

    public void NavigateToExport(string projectDir)
    {
        AppLog.Event("NAV", $"ExportPage sayfasına geçiliyor (Proje: '{projectDir}').");
        RootFrame.Navigate(typeof(ExportPage), projectDir);
    }

    public void NavigateToExport(ScreenPowerPro.Models.ExportOptions options)
    {
        AppLog.Event("NAV", "ExportPage sayfasına geçiliyor (ExportOptions).");
        RootFrame.Navigate(typeof(ExportPage), options);
    }
}
