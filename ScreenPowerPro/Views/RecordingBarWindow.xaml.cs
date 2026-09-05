using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ScreenPowerPro.Helpers;
using ScreenPowerPro.ViewModels;
using Windows.Graphics;

namespace ScreenPowerPro.Views;

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

        // 1. Exclude this floating bar from the recording video via WDA_EXCLUDEFROMCAPTURE
        Win32Helper.SetWindowDisplayAffinity(_hwnd, Win32Helper.WDA_EXCLUDEFROMCAPTURE);

        // 2. Configure window as a compact floating top bar
        var appWindow = AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        // Set size & position at top center
        appWindow.Resize(new SizeInt32(360, 80));
        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            int x = (displayArea.WorkArea.Width - 360) / 2;
            int y = 24;
            appWindow.Move(new PointInt32(x, y));
        }

        // Update timer text on duration change
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
        
        this.Activated += (s, e) => PulseStoryboard.Begin();
    }

    private void OnPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isDragging = true;
        _startPoint = e.GetCurrentPoint(null).Position;
        (sender as UIElement)?.CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
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

    private void OnPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isDragging = false;
        (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
    }

    private async void OnStopClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.StopRecordingAsync();
    }

    private void OnRecordingFinished(string projectDir)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // Close floating bar
            Close();

            // Bring MainWindow to foreground and navigate to editor
            if (MainWindow.CurrentInstance != null)
            {
                MainWindow.CurrentInstance.Activate();
                MainWindow.CurrentInstance.NavigateToEditor(projectDir);
            }
        });
    }
}
