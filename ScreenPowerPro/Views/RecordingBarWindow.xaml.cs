using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using ScreenPowerPro.Helpers;
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
    private bool _isDragging = false;
    private Windows.Foundation.Point _startPoint;

    public RecordingBarWindow(string projectDir)
    {
        InitializeComponent();

        _viewModel = App.Current.Services.GetRequiredService<RecordingBarViewModel>();
        _viewModel.SetActiveProject(projectDir);

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // 1. Çubuğun kendi video kaydında görünmemesi için WDA_EXCLUDEFROMCAPTURE uygula
        Win32Helper.SetWindowDisplayAffinity(_hwnd, Win32Helper.WDA_EXCLUDEFROMCAPTURE);

        // 2. Pencereyi kompakt, çerçevesiz ve her zaman üstte (AlwaysOnTop) yap
        var appWindow = AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        // Boyutlandırma ve ekranın üst merkezine yerleştirme
        appWindow.Resize(new SizeInt32(400, 72));
        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            int x = (displayArea.WorkArea.Width - 400) / 2;
            int y = 20;
            appWindow.Move(new PointInt32(x, y));
        }

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

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = true;
        _startPoint = e.GetCurrentPoint(null).Position;
        (sender as UIElement)?.CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            var currentPoint = e.GetCurrentPoint(null).Position;
            int dx = (int)(currentPoint.X - _startPoint.X);
            int dy = (int)(currentPoint.Y - _startPoint.Y);

            var pos = AppWindow.Position;
            AppWindow.Move(new PointInt32(pos.X + dx, pos.Y + dy));
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
    }

    private async void OnStopClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.StopRecordingAsync();
    }

    private async void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        // Kaydı durdur ve pencereyi kapat, düzenleyiciye yönlendirme yapma
        await _viewModel.StopRecordingAsync();
        Close();

        if (MainWindow.CurrentInstance != null)
        {
            MainWindow.CurrentInstance.Activate();
            MainWindow.CurrentInstance.NavigateToDashboard();
        }
    }

    private void OnRecordingFinished(string projectDir)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Close();

            if (MainWindow.CurrentInstance != null)
            {
                MainWindow.CurrentInstance.Activate();
                MainWindow.CurrentInstance.NavigateToEditor(projectDir);
            }
        });
    }
}
