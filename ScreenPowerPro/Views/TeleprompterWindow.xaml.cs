using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace ScreenPowerPro.Views;

public sealed partial class TeleprompterWindow : Window
{
    private bool _isDragging = false;
    private Windows.Foundation.Point _startPoint;
    private readonly DispatcherTimer _scrollTimer;
    private bool _isScrolling = false;

    public TeleprompterWindow()
    {
        InitializeComponent();

        var appWindow = AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        appWindow.Resize(new SizeInt32(520, 360));
        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            int x = (displayArea.WorkArea.Width - 520) / 2;
            int y = 80;
            appWindow.Move(new PointInt32(x, y));
        }

        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _scrollTimer.Tick += OnScrollTick;
    }

    private void OnScrollTick(object? sender, object e)
    {
        if (!_isScrolling) return;

        double speed = SliderSpeed.Value;
        double currentOffset = ScriptScrollViewer.VerticalOffset;
        double newOffset = currentOffset + (speed * 0.8);
        ScriptScrollViewer.ChangeView(null, newOffset, null, true);
    }

    private void OnHeaderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = true;
        _startPoint = e.GetCurrentPoint(null).Position;
        (sender as UIElement)?.CapturePointer(e.Pointer);
    }

    private void OnHeaderPointerMoved(object sender, PointerRoutedEventArgs e)
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

    private void OnHeaderPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
        (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        _scrollTimer.Stop();
        Close();
    }

    private void OnDecreaseFontSize(object sender, RoutedEventArgs e)
    {
        if (ScriptTextBox.FontSize > 12)
        {
            ScriptTextBox.FontSize -= 2;
            TbFontSize.Text = ScriptTextBox.FontSize.ToString("0");
        }
    }

    private void OnIncreaseFontSize(object sender, RoutedEventArgs e)
    {
        if (ScriptTextBox.FontSize < 48)
        {
            ScriptTextBox.FontSize += 2;
            TbFontSize.Text = ScriptTextBox.FontSize.ToString("0");
        }
    }

    private void OnToggleScroll(object sender, RoutedEventArgs e)
    {
        _isScrolling = !_isScrolling;
        if (_isScrolling)
        {
            _scrollTimer.Start();
            IconPlayPause.Glyph = "\uE769"; // Pause icon
            TbPlayPause.Text = "Durdur";
        }
        else
        {
            _scrollTimer.Stop();
            IconPlayPause.Glyph = "\uE768"; // Play icon
            TbPlayPause.Text = "Başlat";
        }
    }

    private void OnResetScroll(object sender, RoutedEventArgs e)
    {
        ScriptScrollViewer.ChangeView(null, 0, null, false);
    }
}
