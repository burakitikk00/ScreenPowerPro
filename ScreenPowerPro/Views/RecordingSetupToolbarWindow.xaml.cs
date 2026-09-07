using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ScreenPowerPro.Helpers;
using ScreenPowerPro.Models;
using ScreenPowerPro.Services;
using ScreenPowerPro.ViewModels;
using Windows.Foundation;
using Windows.Graphics;

namespace ScreenPowerPro.Views;

public sealed partial class RecordingSetupToolbarWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly DeviceManagerService _deviceManager;
    private readonly DashboardViewModel _viewModel;

    private bool _isDragging = false;
    private PointInt32 _dragStartPoint;
    private PointInt32 _windowStartPoint;

    public RecordingSetupToolbarWindow()
    {
        InitializeComponent();

        _settingsService = App.Current.Services.GetRequiredService<SettingsService>();
        _deviceManager = App.Current.Services.GetRequiredService<DeviceManagerService>();
        _viewModel = App.Current.Services.GetRequiredService<DashboardViewModel>();

        ExtendsContentIntoTitleBar = true;

        var appWindow = AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
        }

        // Toolbar dimensions: 720 x 56
        int width = 720;
        int height = 56;
        appWindow.Resize(new SizeInt32(width, height));

        // Position horizontally centered near bottom of screen
        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            int x = (displayArea.WorkArea.Width - width) / 2;
            int y = displayArea.WorkArea.Y + displayArea.WorkArea.Height - height - 35;
            appWindow.Move(new PointInt32(Math.Max(0, x), Math.Max(0, y)));
        }

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Win32Helper.SetWindowDisplayAffinity(hwnd, Win32Helper.WDA_EXCLUDEFROMCAPTURE);

        GearFlyout.Opening += (s, e) => RefreshMenuCheckmarks();

        RefreshMenuCheckmarks();
        RefreshDeviceLabels();

        _deviceManager.DevicesUpdated += () =>
        {
            DispatcherQueue.TryEnqueue(RefreshDeviceLabels);
        };
    }

    #region Window Dragging

    private void OnToolbarPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(null);
        if (pt.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            Win32Helper.GetCursorPos(out var winPt);
            _dragStartPoint = new PointInt32(winPt.x, winPt.y);
            _windowStartPoint = AppWindow.Position;
            ((UIElement)sender).CapturePointer(e.Pointer);
        }
    }

    private void OnToolbarPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            Win32Helper.GetCursorPos(out var winPt);
            int deltaX = winPt.x - _dragStartPoint.X;
            int deltaY = winPt.y - _dragStartPoint.Y;
            AppWindow.Move(new PointInt32(_windowStartPoint.X + deltaX, _windowStartPoint.Y + deltaY));
        }
    }

    private void OnToolbarPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
    }

    #endregion

    #region Device Labels & Dropdown Menus

    private void RefreshDeviceLabels()
    {
        TbCameraName.Text = _deviceManager.SelectedCamera?.Name ?? "None";
        TbMicName.Text = _deviceManager.SelectedMicrophone?.Name ?? "None";
        TbSpeakerName.Text = _deviceManager.SelectedSpeaker?.Name ?? "None";
    }

    private void OnCameraClicked(object sender, RoutedEventArgs e)
    {
        CameraFlyout.Items.Clear();

        // "None" option
        var noneItem = new MenuFlyoutItem { Text = "None" };
        noneItem.Click += (s, ev) =>
        {
            _deviceManager.SelectCamera("none");
            RefreshDeviceLabels();
        };
        CameraFlyout.Items.Add(noneItem);

        foreach (var cam in _deviceManager.Cameras)
        {
            var item = new MenuFlyoutItem { Text = cam.Name, Tag = cam.Id };
            item.Click += (s, ev) =>
            {
                if (s is MenuFlyoutItem mfi && mfi.Tag is string id)
                {
                    _deviceManager.SelectCamera(id);
                    RefreshDeviceLabels();
                }
            };
            CameraFlyout.Items.Add(item);
        }
    }

    private void OnMicClicked(object sender, RoutedEventArgs e)
    {
        MicFlyout.Items.Clear();

        var noneItem = new MenuFlyoutItem { Text = "None" };
        noneItem.Click += (s, ev) =>
        {
            _deviceManager.SelectMicrophone("none");
            RefreshDeviceLabels();
        };
        MicFlyout.Items.Add(noneItem);

        foreach (var mic in _deviceManager.Microphones)
        {
            var item = new MenuFlyoutItem { Text = mic.Name, Tag = mic.Id };
            item.Click += (s, ev) =>
            {
                if (s is MenuFlyoutItem mfi && mfi.Tag is string id)
                {
                    _deviceManager.SelectMicrophone(id);
                    RefreshDeviceLabels();
                }
            };
            MicFlyout.Items.Add(item);
        }
    }

    private void OnSpeakerClicked(object sender, RoutedEventArgs e)
    {
        SpeakerFlyout.Items.Clear();

        var noneItem = new MenuFlyoutItem { Text = "None" };
        noneItem.Click += (s, ev) =>
        {
            _deviceManager.SelectSpeaker("none");
            RefreshDeviceLabels();
        };
        SpeakerFlyout.Items.Add(noneItem);

        foreach (var spk in _deviceManager.Speakers)
        {
            var item = new MenuFlyoutItem { Text = spk.Name, Tag = spk.Id };
            item.Click += (s, ev) =>
            {
                if (s is MenuFlyoutItem mfi && mfi.Tag is string id)
                {
                    _deviceManager.SelectSpeaker(id);
                    RefreshDeviceLabels();
                }
            };
            SpeakerFlyout.Items.Add(item);
        }
    }

    #endregion

    #region Gear Settings Menu

    private void RefreshMenuCheckmarks()
    {
        var s = _settingsService.Current;

        // Zoom Effect
        ItemZoomNone.IsChecked = s.ZoomEffect == "None";
        ItemZoom2D.IsChecked = s.ZoomEffect == "2D Zoom";
        ItemZoom3D.IsChecked = s.ZoomEffect == "3D Motion";

        // Countdown
        ItemCount3s.IsChecked = s.CountdownSeconds == 3;
        ItemCount5s.IsChecked = s.CountdownSeconds == 5;
        ItemCountNone.IsChecked = s.CountdownSeconds == 0;

        // Toggles
        ItemHideIcons.IsChecked = s.HideDesktopIcons;
        ItemHideTaskbar.IsChecked = s.HideTaskbar;
    }

    private void OnSelectZoomNone(object sender, RoutedEventArgs e)
    {
        _settingsService.Current.ZoomEffect = "None";
        _settingsService.Save();
        RefreshMenuCheckmarks();
    }

    private void OnSelectZoom2D(object sender, RoutedEventArgs e)
    {
        _settingsService.Current.ZoomEffect = "2D Zoom";
        _settingsService.Save();
        RefreshMenuCheckmarks();
    }

    private void OnSelectZoom3D(object sender, RoutedEventArgs e)
    {
        _settingsService.Current.ZoomEffect = "3D Motion";
        _settingsService.Save();
        RefreshMenuCheckmarks();
    }

    private void OnSelectCount3s(object sender, RoutedEventArgs e)
    {
        _settingsService.Current.CountdownSeconds = 3;
        _settingsService.Save();
        RefreshMenuCheckmarks();
    }

    private void OnSelectCount5s(object sender, RoutedEventArgs e)
    {
        _settingsService.Current.CountdownSeconds = 5;
        _settingsService.Save();
        RefreshMenuCheckmarks();
    }

    private void OnSelectCountNone(object sender, RoutedEventArgs e)
    {
        _settingsService.Current.CountdownSeconds = 0;
        _settingsService.Save();
        RefreshMenuCheckmarks();
    }

    private void OnToggleHideIcons(object sender, RoutedEventArgs e)
    {
        bool newVal = !_settingsService.Current.HideDesktopIcons;
        _settingsService.Current.HideDesktopIcons = newVal;
        _settingsService.Save();
        Win32Helper.SetDesktopIconsVisible(!newVal);
        RefreshMenuCheckmarks();
    }

    private void OnToggleHideTaskbar(object sender, RoutedEventArgs e)
    {
        bool newVal = !_settingsService.Current.HideTaskbar;
        _settingsService.Current.HideTaskbar = newVal;
        _settingsService.Save();
        Win32Helper.SetTaskbarVisible(!newVal);
        RefreshMenuCheckmarks();
    }

    private void OnOpenMoreSettings(object sender, RoutedEventArgs e)
    {
        var settingsWin = new SettingsWindow();
        settingsWin.Activate();
    }

    #endregion

    #region Close & REC Actions

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();

        // Close border if open
        FullScreenBorderWindow.Instance?.Close();

        // Restore Dashboard MainWindow
        if (MainWindow.CurrentInstance != null)
        {
            var appWin = MainWindow.CurrentInstance.AppWindow;
            if (appWin.Presenter is OverlappedPresenter p)
            {
                p.Restore();
            }
            MainWindow.CurrentInstance.Activate();
        }
    }

    private void OnRecClicked(object sender, RoutedEventArgs e)
    {
        int countdown = _settingsService.Current.CountdownSeconds;

        // Hide toolbar and border window before starting
        Close();
        FullScreenBorderWindow.Instance?.Close();

        if (countdown > 0)
        {
            var countdownWindow = new CountdownWindow(countdown, async () =>
            {
                await _viewModel.StartRecordingAsync();
            });
            countdownWindow.Activate();
        }
        else
        {
            _ = _viewModel.StartRecordingAsync();
        }
    }

    #endregion
}
