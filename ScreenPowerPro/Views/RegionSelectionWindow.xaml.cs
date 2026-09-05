using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using ScreenPowerPro.Helpers;

namespace ScreenPowerPro.Views;

public sealed partial class RegionSelectionWindow : Window
{
    private bool _isDrawing = false;
    private Point _startPoint;
    private Rect _selectedRegion;
    private readonly TaskCompletionSource<Rect?> _tcs = new();

    public RegionSelectionWindow()
    {
        InitializeComponent();

        var appWindow = AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        ExtendsContentIntoTitleBar = true;

        // Make window fullscreen covering all displays
        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            appWindow.MoveAndResize(new RectInt32(
                displayArea.WorkArea.X,
                displayArea.WorkArea.Y,
                displayArea.WorkArea.Width,
                displayArea.WorkArea.Height));
        }

        // Exclude this window from capture so it doesn't flash in recordings, though it should be closed by then.
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Win32Helper.SetWindowDisplayAffinity(hwnd, Win32Helper.WDA_EXCLUDEFROMCAPTURE);
        
        // Wait for it to be ready
        RootGrid.Loaded += (s, e) =>
        {
            InstructionBadge.Visibility = Visibility.Visible;
            Canvas.SetLeft(InstructionBadge, (RootGrid.ActualWidth - InstructionBadge.ActualWidth) / 2);
            Canvas.SetTop(InstructionBadge, (RootGrid.ActualHeight - InstructionBadge.ActualHeight) / 2);
        };
    }

    public Task<Rect?> WaitForSelectionAsync()
    {
        return _tcs.Task;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isDrawing = true;
        _startPoint = e.GetCurrentPoint(SelectionCanvas).Position;
        SelectionRect.Visibility = Visibility.Visible;
        SizeBadge.Visibility = Visibility.Visible;
        InstructionBadge.Visibility = Visibility.Collapsed;
        UpdateSelectionRect(_startPoint);
        RootGrid.CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDrawing) return;

        var currentPoint = e.GetCurrentPoint(SelectionCanvas).Position;
        UpdateSelectionRect(currentPoint);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDrawing) return;
        _isDrawing = false;
        RootGrid.ReleasePointerCapture(e.Pointer);

        if (_selectedRegion.Width > 50 && _selectedRegion.Height > 50)
        {
            _tcs.TrySetResult(_selectedRegion);
        }
        else
        {
            // Too small, probably a mistaken click. Cancel it.
            _tcs.TrySetResult(null);
        }
        Close();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            _tcs.TrySetResult(null);
            Close();
        }
    }

    private void UpdateSelectionRect(Point currentPoint)
    {
        double x = Math.Min(_startPoint.X, currentPoint.X);
        double y = Math.Min(_startPoint.Y, currentPoint.Y);
        double width = Math.Abs(_startPoint.X - currentPoint.X);
        double height = Math.Abs(_startPoint.Y - currentPoint.Y);

        _selectedRegion = new Rect(x, y, width, height);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = width;
        SelectionRect.Height = height;

        SizeText.Text = $"{(int)width} x {(int)height}";
        Canvas.SetLeft(SizeBadge, x + width - SizeBadge.ActualWidth);
        Canvas.SetTop(SizeBadge, y + height + 5);

        UpdateOverlay();
    }

    private void UpdateOverlay()
    {
        var geometryGroup = new GeometryGroup();
        
        // Full screen
        geometryGroup.Children.Add(new RectangleGeometry { Rect = new Rect(0, 0, RootGrid.ActualWidth, RootGrid.ActualHeight) });
        
        // Excluded region
        geometryGroup.Children.Add(new RectangleGeometry { Rect = _selectedRegion });

        geometryGroup.FillRule = FillRule.EvenOdd;
        OverlayPath.Data = geometryGroup;
    }
}
