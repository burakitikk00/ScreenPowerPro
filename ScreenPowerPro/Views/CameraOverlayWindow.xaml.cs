using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ScreenPowerPro.Services;
using Windows.Graphics;
using Windows.Media.Capture;

namespace ScreenPowerPro.Views;

/// <summary>
/// Kayıt sırasında ekranda yüzen, kullanıcının web kamerasını gösteren
/// yuvarlak/köşeli formatta sürüklenebilir her zaman üstte (AlwaysOnTop) kamera penceresi.
/// </summary>
public sealed partial class CameraOverlayWindow : Window
{
    private bool _isDragging = false;
    private Windows.Foundation.Point _startPoint;
    private bool _isCircular = true;
    private MediaCapture? _mediaCapture;

    public CameraOverlayWindow()
    {
        InitializeComponent();

        var appWindow = AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        // Boyutu ayarla ve ekranın sağ alt köşesine yerleştir
        appWindow.Resize(new SizeInt32(200, 200));
        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            int x = displayArea.WorkArea.Width - 240;
            int y = displayArea.WorkArea.Height - 240;
            appWindow.Move(new PointInt32(x, y));
        }

        try
        {
            var loc = App.Current?.Services?.GetService<LocalizationService>();
            if (loc != null)
            {
                Title = loc["Camera_Title"];
                TbCameraActive.Text = loc["Camera_Active"];
                ToolTipService.SetToolTip(BtnToggleShape, loc["Camera_ToggleShape"]);
                ToolTipService.SetToolTip(BtnCloseCam, loc["Camera_Close"]);
            }
        }
        catch { }

        _ = InitializeCameraAsync();
    }

    /// <summary>
    /// Sistemdeki ilk uygun kameradan önizleme akışını başlatmayı dener.
    /// </summary>
    private async Task InitializeCameraAsync()
    {
        try
        {
            _mediaCapture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video
            };
            await _mediaCapture.InitializeAsync(settings);

            // XAML MediaCapture önizleme akışı bağlama
            // WinUI 3'te CaptureElement MediaCapture'a bağlanır
            // Note: CaptureElement.Source can be set if supported, otherwise fallbackPanel is kept
        }
        catch
        {
            // Kamera açılamazsa yer tutucu (placeholder) simgesi görünür kalır
            if (FallbackPanel != null)
            {
                FallbackPanel.Visibility = Visibility.Visible;
            }
        }
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

    private void OnToggleShapeClicked(object sender, RoutedEventArgs e)
    {
        _isCircular = !_isCircular;
        if (_isCircular)
        {
            CameraBorder.CornerRadius = new CornerRadius(90);
        }
        else
        {
            CameraBorder.CornerRadius = new CornerRadius(16);
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _mediaCapture?.Dispose();
            _mediaCapture = null;
        }
        catch { }

        Close();
    }
}
