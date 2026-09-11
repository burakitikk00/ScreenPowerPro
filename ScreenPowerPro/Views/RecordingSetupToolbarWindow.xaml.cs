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
    private readonly LocalizationService _loc;

    private readonly IntPtr _hwnd;
    private bool _hasPositioned = false;
    private bool _isDragging = false;
    private PointInt32 _dragStartPoint;
    private PointInt32 _windowStartPoint;

    public RecordingSetupToolbarWindow()
    {
        InitializeComponent();

        _settingsService = App.Current.Services.GetRequiredService<SettingsService>();
        _deviceManager = App.Current.Services.GetRequiredService<DeviceManagerService>();
        _viewModel = App.Current.Services.GetRequiredService<DashboardViewModel>();
        _loc = App.Current.Services.GetRequiredService<LocalizationService>();

        var appWindow = AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
        }

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Win32Helper.SetWindowDisplayAffinity(_hwnd, Win32Helper.WDA_EXCLUDEFROMCAPTURE);

        GearFlyout.Opening += (s, e) => RefreshMenuCheckmarks();

        _loc.LanguageChanged += ApplyLocalization;
        ApplyLocalization();

        Closed += (s, e) =>
        {
            _loc.LanguageChanged -= ApplyLocalization;
        };

        RefreshMenuCheckmarks();
        RefreshDeviceLabels();

        _deviceManager.DevicesUpdated += () =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                RefreshDeviceLabels();
                UpdateToolbarSize();
            });
        };

        ToolbarBorder.SizeChanged += (s, e) => UpdateToolbarSize();
        ToolbarBorder.Loaded += (s, e) => UpdateToolbarSize();

        UpdateToolbarSize();
    }

    private void ApplyLocalization()
    {
        if (BtnClose != null) ToolTipService.SetToolTip(BtnClose, _loc["Setup_Close"]);
        if (ModeIcon != null) ToolTipService.SetToolTip(ModeIcon, _loc["Setup_FullScreen"]);
        if (FlyoutSubZoom != null) FlyoutSubZoom.Text = _loc["Setup_ZoomEffect"];
        if (ItemZoomNone != null) ItemZoomNone.Text = _loc["Setup_ZoomNone"];
        if (ItemZoom2D != null) ItemZoom2D.Text = _loc["Setup_Zoom2D"];
        if (ItemZoom3D != null) ItemZoom3D.Text = _loc["Setup_Zoom3D"];
        if (FlyoutSubCountdown != null) FlyoutSubCountdown.Text = _loc["Setup_Countdown"];
        if (ItemCount3s != null) ItemCount3s.Text = _loc["Setup_Count3s"];
        if (ItemCount5s != null) ItemCount5s.Text = _loc["Setup_Count5s"];
        if (ItemCountNone != null) ItemCountNone.Text = _loc["Setup_CountNone"];
        if (ItemHideIcons != null) ItemHideIcons.Text = _loc["Setup_HideIcons"];
        if (ItemHideTaskbar != null) ItemHideTaskbar.Text = _loc["Setup_HideTaskbar"];
        if (ItemMoreSettings != null) ItemMoreSettings.Text = _loc["Setup_MoreSettings"];
        if (TbStartRecText != null) TbStartRecText.Text = _loc["Setup_Start"];
        if (BtnStartRec != null) ToolTipService.SetToolTip(BtnStartRec, _loc["Setup_Start"]);
        RefreshDeviceLabels();
    }

    private void UpdateToolbarSize()
    {
        if (ToolbarBorder == null) return;

        ToolbarBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double desiredDipWidth = ToolbarBorder.DesiredSize.Width;
        double desiredDipHeight = Math.Max(ToolbarBorder.DesiredSize.Height, 46);

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
            int workAreaHeight = displayArea?.WorkArea.Height ?? 1080;
            int workAreaY = displayArea?.WorkArea.Y ?? 0;

            newX = (workAreaWidth - newWidth) / 2;
            newY = workAreaY + workAreaHeight - newHeight - (int)(30 * scale);
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
        string none = _loc["None"];
        TbCameraName.Text = _deviceManager.SelectedCamera?.Name ?? none;
        TbMicName.Text = _deviceManager.SelectedMicrophone?.Name ?? none;
        TbSpeakerName.Text = _deviceManager.SelectedSpeaker?.Name ?? none;
    }

    private void OnCameraClicked(object sender, RoutedEventArgs e)
    {
        CameraFlyout.Items.Clear();

        // "None" option
        var noneItem = new MenuFlyoutItem { Text = _loc["None"] };
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

        var noneItem = new MenuFlyoutItem { Text = _loc["None"] };
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

        var noneItem = new MenuFlyoutItem { Text = _loc["None"] };
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
        RefreshMenuCheckmarks();
    }

    private void OnToggleHideTaskbar(object sender, RoutedEventArgs e)
    {
        bool newVal = !_settingsService.Current.HideTaskbar;
        _settingsService.Current.HideTaskbar = newVal;
        _settingsService.Save();
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
            var countdownWindow = new CountdownWindow(countdown, () =>
            {
                if (MainWindow.CurrentInstance != null)
                {
                    MainWindow.CurrentInstance.DispatcherQueue.TryEnqueue(async () =>
                    {
                        await _viewModel.StartRecordingAsync();
                    });
                }
                else
                {
                    _ = _viewModel.StartRecordingAsync();
                }
            });
            countdownWindow.Activate();
        }
        else
        {
            if (MainWindow.CurrentInstance != null)
            {
                MainWindow.CurrentInstance.DispatcherQueue.TryEnqueue(async () =>
                {
                    await _viewModel.StartRecordingAsync();
                });
            }
            else
            {
                _ = _viewModel.StartRecordingAsync();
            }
        }
    }

    #endregion
}
