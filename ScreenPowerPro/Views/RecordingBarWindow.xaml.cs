using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ScreenPowerPro.Helpers;
using ScreenPowerPro.Services;
using ScreenPowerPro.ViewModels;
using Windows.Graphics;

namespace ScreenPowerPro.Views;

/// <summary>
/// Kayıt sırasında ekranın üst kısmında yüzen, kullanıcının kaydı durdurmasını,
/// duraklatmasını veya iptal etmesini sağlayan taşınabilir kompakt araç çubuğu.
/// </summary>
public sealed partial class RecordingBarWindow : Window
{
    private readonly RecordingBarViewModel _viewModel;
    private readonly IntPtr _hwnd;
    private bool _hasPositioned = false;
    private bool _isDragging = false;
    private PointInt32 _dragStartPoint;
    private PointInt32 _windowStartPoint;
    private readonly LocalizationService _loc;

    public RecordingBarWindow(string projectDir, int width = 1920, int height = 1080, int originX = 0, int originY = 0)
    {
        InitializeComponent();

        _viewModel = App.Current.Services.GetRequiredService<RecordingBarViewModel>();
        _loc = App.Current.Services.GetRequiredService<LocalizationService>();
        _viewModel.SetActiveProject(projectDir, width, height, originX, originY);

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // 1. Çubuğun kendi video kaydında görünmemesi için WDA_EXCLUDEFROMCAPTURE uygula
        Win32Helper.SetWindowDisplayAffinity(_hwnd, Win32Helper.WDA_EXCLUDEFROMCAPTURE);

        // 2. Pencereyi kompakt, çerçevesiz ve her zaman üstte (AlwaysOnTop) yap
        var appWindow = AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        // Dinamik boyutlandırma dinleyicileri
        BarBorder.SizeChanged += (s, e) => UpdateBarSize();
        BarBorder.Loaded += (s, e) => UpdateBarSize();
        UpdateBarSize();

        _loc.LanguageChanged += ApplyLocalization;
        ApplyLocalization();

        Closed += (s, e) =>
        {
            _loc.LanguageChanged -= ApplyLocalization;
        };

        // Süre güncellemesini dinle
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(RecordingBarViewModel.ElapsedTime))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    TimerTextBlock.Text = _viewModel.ElapsedTime;
                });
            }
        };

        _viewModel.RecordingFinished += OnRecordingFinished;

        Activated += (s, e) => PulseStoryboard.Begin();
    }

    private void ApplyLocalization()
    {
        if (StatusTextBlock != null) StatusTextBlock.Text = _loc.CurrentLanguage == "en" ? "REC" : "KAYIT";
        if (BtnPause != null) ToolTipService.SetToolTip(BtnPause, _loc["RecBar_Pause"]);
        if (BtnStop != null) ToolTipService.SetToolTip(BtnStop, _loc["RecBar_Finish"]);
        if (TbStopNormalText != null) TbStopNormalText.Text = _loc.CurrentLanguage == "en" ? "Finish Recording" : "Kaydı Bitir";
        if (TbStopLoadingText != null) TbStopLoadingText.Text = _loc["RecBar_Loading"];
        if (BtnCancelRec != null) ToolTipService.SetToolTip(BtnCancelRec, _loc["RecBar_Cancel"]);
    }

    private void UpdateBarSize()
    {
        if (BarBorder == null) return;

        BarBorder.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double desiredDipWidth = BarBorder.DesiredSize.Width;
        double desiredDipHeight = Math.Max(BarBorder.DesiredSize.Height, 46);

        if (desiredDipWidth < 50) return;

        uint dpi = Win32Helper.GetDpiForWindow(_hwnd);
        float scale = dpi > 0 ? dpi / 96f : 1.0f;

        int newWidth = (int)Math.Ceiling(desiredDipWidth * scale);
        int newHeight = (int)Math.Ceiling(desiredDipHeight * scale);

        var appWindow = AppWindow;
        var curSize = appWindow.Size;
        var curPos = appWindow.Position;

        if (Math.Abs(curSize.Width - newWidth) < 2 && Math.Abs(curSize.Height - newHeight) < 2 && _hasPositioned)
        {
            return;
        }

        int newX;
        int newY;

        if (!_hasPositioned)
        {
            var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            int workAreaWidth = displayArea?.WorkArea.Width ?? 1920;
            int workAreaY = displayArea?.WorkArea.Y ?? 0;

            newX = (workAreaWidth - newWidth) / 2;
            newY = workAreaY + (int)(18 * scale);
            _hasPositioned = true;
        }
        else
        {
            int curCenterX = curPos.X + curSize.Width / 2;
            newX = curCenterX - newWidth / 2;
            newY = curPos.Y;
        }

        appWindow.MoveAndResize(new RectInt32(newX, newY, newWidth, newHeight));
        Win32Helper.ApplyRoundedCorners(_hwnd, newWidth, newHeight, (int)(16 * scale));
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(null);
        if (pt.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            Win32Helper.GetCursorPos(out var winPt);
            _dragStartPoint = new PointInt32(winPt.x, winPt.y);
            _windowStartPoint = AppWindow.Position;
            (sender as UIElement)?.CapturePointer(e.Pointer);
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            Win32Helper.GetCursorPos(out var winPt);
            int deltaX = winPt.x - _dragStartPoint.X;
            int deltaY = winPt.y - _dragStartPoint.Y;
            AppWindow.Move(new PointInt32(_windowStartPoint.X + deltaX, _windowStartPoint.Y + deltaY));
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
        (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
    }

    private void OnPauseClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.TogglePause();
        if (_viewModel.IsPaused)
        {
            PauseIcon.Glyph = "\uE768"; // Play Glyph
            StatusTextBlock.Text = "DURAKLATILDI";
            PulseStoryboard.Pause();
        }
        else
        {
            PauseIcon.Glyph = "\uE769"; // Pause Glyph
            StatusTextBlock.Text = "KAYIT";
            PulseStoryboard.Resume();
        }
        UpdateBarSize();
    }

    private async void OnStopClicked(object sender, RoutedEventArgs e)
    {
        // 1. Durdur butonunu loading ve yanıp sönme durumuna geçir
        BtnStop.IsHitTestVisible = false; // Tekrar tıklanmayı önle, ancak butonun canlı renklerini koru
        BtnPause.IsEnabled = false;

        StopNormalPanel.Visibility = Visibility.Collapsed;
        StopLoadingPanel.Visibility = Visibility.Visible;
        StopLoadingStoryboard.Begin();

        StatusTextBlock.Text = "KAYDEDİLİYOR...";
        PulseStoryboard.Pause();
        UpdateBarSize();
        AppLog.Event("RECORDING", "Kullanıcı kaydı durdur butonuna bastı. StopRecordingAsync çağrılıyor...");

        try
        {
            await _viewModel.StopRecordingAsync();
            AppLog.Success("StopRecordingAsync çağrısı başarıyla tamamlandı.");
        }
        catch (Exception ex)
        {
            AppLog.Error("StopRecordingAsync sırasında hata meydana geldi!", ex);
            // Olası hata durumunda kilitlenmeyi önle
            BtnStop.IsHitTestVisible = true;
            StopLoadingStoryboard.Stop();
            StopLoadingPanel.Visibility = Visibility.Collapsed;
            StopNormalPanel.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "HATA";
            UpdateBarSize();
        }
    }

    private async void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        BtnStop.IsHitTestVisible = false;
        BtnPause.IsEnabled = false;
        StatusTextBlock.Text = "İPTAL EDİLİYOR...";
        PulseStoryboard.Pause();
        UpdateBarSize();
        AppLog.Event("RECORDING", "Kayıt iptal edildi.");

        // Kaydı durdur ve pencereyi kapat, düzenleyiciye yönlendirme yapma
        await _viewModel.StopRecordingAsync();

        if (MainWindow.CurrentInstance != null)
        {
            var mainWin = MainWindow.CurrentInstance;
            mainWin.DispatcherQueue.TryEnqueue(() =>
            {
                var appWin = mainWin.AppWindow;
                if (appWin.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Restore();
                }

                var hwnd = mainWin.GetWindowHandle();
                Win32Helper.ShowWindow(hwnd, Win32Helper.SW_RESTORE);
                Win32Helper.SetForegroundWindow(hwnd);

                mainWin.Activate();
                mainWin.NavigateToDashboard();
                Close();
            });
        }
        else
        {
            Close();
        }
    }

    private void OnRecordingFinished(string projectDir)
    {
        AppLog.Event("RECORDING", $"Kayıt tamamlandı bildirimi alındı. Proje dizini: '{projectDir}'");
        if (MainWindow.CurrentInstance != null)
        {
            var mainWin = MainWindow.CurrentInstance;
            mainWin.DispatcherQueue.TryEnqueue(() =>
            {
                AppLog.Info("Ana pencere DispatcherQueue ile geri getiriliyor ve Editor'e geçiliyor...");
                var appWin = mainWin.AppWindow;
                if (appWin.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Restore();
                    presenter.IsResizable = true;
                    presenter.IsMaximizable = true;
                }

                var hwnd = mainWin.GetWindowHandle();
                Win32Helper.ShowWindow(hwnd, Win32Helper.SW_RESTORE);
                Win32Helper.SetForegroundWindow(hwnd);

                mainWin.Activate();
                AppLog.Info($"Ana pencere aktif. mainWin.NavigateToEditor('{projectDir}') çağrılıyor...");
                mainWin.NavigateToEditor(projectDir);

                // Ana pencere başarıyla geri yüklenip düzenleyiciye geçtikten sonra çubuğu kapat
                AppLog.Info("RecordingBarWindow kapatılıyor.");
                Close();
            });
        }
        else
        {
            AppLog.Warn("MainWindow.CurrentInstance NULL, doğrudan kapatılıyor.");
            Close();
        }
    }
}
