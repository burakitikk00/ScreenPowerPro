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
        AppWindow.Closing += AppWindow_Closing;
    }

    private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (RootFrame.Content is EditorPage editorPage)
        {
            if (!editorPage.IsReadyToClose)
            {
                args.Cancel = true;
                editorPage.ShowExitConfirmationOverlay();
            }
        }
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
        HideProcessingOverlay();
        ResizeForDashboard();
        RootFrame.Navigate(typeof(DashboardPage), null, new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
    }

    public void NavigateToLibrary()
    {
        AppLog.Event("NAV", "LibraryPage sayfasına geçiliyor.");
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Restore();
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }
        ResizeAndCenter(1160, 600);
        RootFrame.Navigate(typeof(LibraryPage), null, new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
    }

    public async void NavigateToEditor(string projectDir)
    {
        AppLog.Event("NAV", $"EditorPage sayfasına geçiliyor. Proje: {projectDir}");
        try
        {
            ShowProcessingOverlay();
            ResizeForEditor();
            
            await System.Threading.Tasks.Task.Delay(1500); // 1.5 saniye animasyon göster (siyah ekranları gizler)

            bool result = RootFrame.Navigate(typeof(EditorPage), projectDir, new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
            AppLog.Success($"EditorPage navigasyonu tamamlandı (Result: {result}).");

            await System.Threading.Tasks.Task.Delay(500); // UI render süresi için ek bekleme
            HideProcessingOverlay();
        }
        catch (Exception ex)
        {
            AppLog.Error($"[MainWindow] EditorPage sayfasına navigasyon sırasında KRİTİK HATA!", ex);
            HideProcessingOverlay();
        }
    }

    /// <summary>
    /// Kayıt işlemi tamamlanırken gösterilen tam ekran yükleme overlay'ini gösterir.
    /// Herhangi bir thread'den güvenle çağrılabilir.
    /// </summary>
    public void ShowProcessingOverlay()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var anim = RootGrid.Resources["LoadingMainAnimation"] as Microsoft.UI.Xaml.Media.Animation.Storyboard;
            anim?.Begin();
            LoadingOverlay.Visibility = Visibility.Visible;
        });
    }

    public void HideProcessingOverlay()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var anim = RootGrid.Resources["LoadingMainAnimation"] as Microsoft.UI.Xaml.Media.Animation.Storyboard;
            anim?.Stop();
            LoadingOverlay.Visibility = Visibility.Collapsed;
        });
    }
}
