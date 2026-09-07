using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ScreenPowerPro.Helpers;
using Windows.Graphics;

namespace ScreenPowerPro.Views;

/// <summary>
/// Kayıt başlamadan önce ekranda beliren, kullanıcıyı hazırlayan ve
/// süresi dolduğunda kaydı tetikleyen tam ekran yarı-saydam geri sayım penceresi.
/// </summary>
public sealed partial class CountdownWindow : Window
{
    private readonly int _totalSeconds;
    private int _remainingSeconds;
    private readonly DispatcherTimer _timer;
    private readonly Action _onCompleted;

    public CountdownWindow(int countdownSeconds, Action onCompleted)
    {
        InitializeComponent();

        _totalSeconds = countdownSeconds <= 0 ? 3 : countdownSeconds;
        _remainingSeconds = _totalSeconds;
        _onCompleted = onCompleted;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // Pencerenin ekran kaydında görünmesini engelle
        Win32Helper.SetWindowDisplayAffinity(hwnd, Win32Helper.WDA_EXCLUDEFROMCAPTURE);

        // Tıklama geçiren (click-through) moda al
        Win32Helper.SetWindowClickThrough(hwnd);

        // Tam ekran ve kenarlıksız pencere ayarları
        var appWindow = AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.Maximize();
        }

        TbCount.Text = _remainingSeconds.ToString();

        // Her saniye geri sayan timer
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, object e)
    {
        _remainingSeconds--;

        if (_remainingSeconds > 0)
        {
            TbCount.Text = _remainingSeconds.ToString();
        }
        else
        {
            _timer.Stop();
            Close();
            _onCompleted?.Invoke();
        }
    }
}
