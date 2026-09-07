using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScreenPowerPro.Helpers;
using ScreenPowerPro.Services;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace ScreenPowerPro.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private bool _isInitializing = true;

    public SettingsWindow()
    {
        InitializeComponent();

        _settingsService = App.Current.Services.GetRequiredService<SettingsService>();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarGrid);

        var appWindow = AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        // Center on screen with 620 x 530
        int width = 620;
        int height = 530;
        appWindow.Resize(new SizeInt32(width, height));

        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            int x = (displayArea.WorkArea.Width - width) / 2;
            int y = (displayArea.WorkArea.Height - height) / 2;
            appWindow.Move(new PointInt32(Math.Max(0, x), Math.Max(0, y)));
        }

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Win32Helper.SetWindowDisplayAffinity(hwnd, Win32Helper.WDA_EXCLUDEFROMCAPTURE);

        LoadSettingsToUI();
    }

    private void LoadSettingsToUI()
    {
        _isInitializing = true;
        var s = _settingsService.Current;

        // General
        TbSaveLocation.Text = s.ProjectSaveLocation;
        TbExportLocation.Text = s.ExportLocation;
        SwitchAutoStart.IsOn = s.AutoStart || SystemHelper.IsAutoStartEnabled();
        SwitchAutoPlayVideo.IsOn = s.AutoPlayVideo;
        TbGraphicsCard.Text = SystemHelper.GetGraphicsCardName();

        // Record
        SetComboSelection(CmbZoomEffect, s.ZoomEffect);
        SwitchHideDesktopIcons.IsOn = s.HideDesktopIcons;
        SwitchHideTaskbar.IsOn = s.HideTaskbar;
        SetComboSelection(CmbRecordingQuality, s.RecordingQuality);

        string countText = s.CountdownSeconds switch
        {
            5 => "5s",
            0 => "No countdown",
            _ => "3s"
        };
        SetComboSelection(CmbCountdown, countText);

        // Shortcuts
        TbShortcutStartStop.Text = string.IsNullOrEmpty(s.ShortcutStartStop) ? "F9" : s.ShortcutStartStop;
        TbShortcutPause.Text = string.IsNullOrEmpty(s.ShortcutPause) ? "F10" : s.ShortcutPause;
        TbShortcutScreenshot.Text = string.IsNullOrEmpty(s.ShortcutScreenshot) ? "F11" : s.ShortcutScreenshot;

        // Export
        SetComboSelection(CmbExportFormat, s.ExportFormat.ToUpperInvariant());
        SetComboSelection(CmbExportResolution, s.ExportResolution);
        SetComboSelection(CmbExportFps, $"{s.Fps} FPS");

        _isInitializing = false;
    }

    private static void SetComboSelection(ComboBox cmb, string value)
    {
        foreach (var item in cmb.Items)
        {
            if (item is ComboBoxItem cbi && string.Equals(cbi.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                cmb.SelectedItem = cbi;
                return;
            }
        }
        if (cmb.Items.Count > 0)
        {
            cmb.SelectedIndex = 0;
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    #region Tab Navigation

    private void ResetTabs()
    {
        var inactiveBg = new SolidColorBrush(ColorHelper.FromArgb(255, 27, 28, 38));
        var inactiveBorder = new SolidColorBrush(ColorHelper.FromArgb(255, 42, 43, 56));
        var inactiveFg = new SolidColorBrush(ColorHelper.FromArgb(255, 160, 160, 178));

        Button[] buttons = { BtnTabGeneral, BtnTabRecord, BtnTabShortcuts, BtnTabExport };
        foreach (var btn in buttons)
        {
            btn.Background = inactiveBg;
            btn.BorderBrush = inactiveBorder;
            btn.BorderThickness = new Thickness(1);
            if (btn.Content is TextBlock tb)
            {
                tb.Foreground = inactiveFg;
            }
        }

        PanelTabGeneral.Visibility = Visibility.Collapsed;
        PanelTabRecord.Visibility = Visibility.Collapsed;
        PanelTabShortcuts.Visibility = Visibility.Collapsed;
        PanelTabExport.Visibility = Visibility.Collapsed;
    }

    private void SetActiveTab(Button btn, StackPanel panel)
    {
        ResetTabs();

        btn.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 30, 58, 138));
        btn.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 37, 99, 235));
        btn.BorderThickness = new Thickness(1.5);
        if (btn.Content is TextBlock tb)
        {
            tb.Foreground = new SolidColorBrush(Colors.White);
        }

        panel.Visibility = Visibility.Visible;
    }

    private void OnTabGeneralClicked(object sender, RoutedEventArgs e) => SetActiveTab(BtnTabGeneral, PanelTabGeneral);
    private void OnTabRecordClicked(object sender, RoutedEventArgs e) => SetActiveTab(BtnTabRecord, PanelTabRecord);
    private void OnTabShortcutsClicked(object sender, RoutedEventArgs e) => SetActiveTab(BtnTabShortcuts, PanelTabShortcuts);
    private void OnTabExportClicked(object sender, RoutedEventArgs e) => SetActiveTab(BtnTabExport, PanelTabExport);

    #endregion

    #region General Tab Actions

    private async void OnBrowseSaveLocationClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                TbSaveLocation.Text = folder.Path;
                _settingsService.Current.ProjectSaveLocation = folder.Path;
                _settingsService.Save();
            }
        }
        catch { }
    }

    private async void OnBrowseExportLocationClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            picker.FileTypeFilter.Add("*");

            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                TbExportLocation.Text = folder.Path;
                _settingsService.Current.ExportLocation = folder.Path;
                _settingsService.Save();
            }
        }
        catch { }
    }

    private void OnAutoStartToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        bool isOn = SwitchAutoStart.IsOn;
        _settingsService.Current.AutoStart = isOn;
        _settingsService.Save();
        SystemHelper.SetAutoStart(isOn);
    }

    private void OnAutoPlayVideoToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _settingsService.Current.AutoPlayVideo = SwitchAutoPlayVideo.IsOn;
        _settingsService.Save();
    }

    #endregion

    #region Record Tab Actions

    private void OnZoomEffectChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CmbZoomEffect.SelectedItem is ComboBoxItem item && item.Content is string val)
        {
            _settingsService.Current.ZoomEffect = val;
            _settingsService.Save();
        }
    }

    private void OnHideDesktopIconsToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        bool isOn = SwitchHideDesktopIcons.IsOn;
        _settingsService.Current.HideDesktopIcons = isOn;
        _settingsService.Save();
        Win32Helper.SetDesktopIconsVisible(!isOn);
    }

    private void OnHideTaskbarToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        bool isOn = SwitchHideTaskbar.IsOn;
        _settingsService.Current.HideTaskbar = isOn;
        _settingsService.Save();
        Win32Helper.SetTaskbarVisible(!isOn);
    }

    private void OnRecordingQualityChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CmbRecordingQuality.SelectedItem is ComboBoxItem item && item.Content is string val)
        {
            _settingsService.Current.RecordingQuality = val;
            _settingsService.Save();
        }
    }

    private void OnCountdownChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CmbCountdown.SelectedItem is ComboBoxItem item && item.Content is string val)
        {
            _settingsService.Current.CountdownSeconds = val switch
            {
                "3s" => 3,
                "5s" => 5,
                _ => 0
            };
            _settingsService.Save();
        }
    }

    #endregion

    #region Shortcut & Export Actions

    private void OnShortcutChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settingsService.Current.ShortcutStartStop = TbShortcutStartStop.Text.Trim();
        _settingsService.Current.ShortcutPause = TbShortcutPause.Text.Trim();
        _settingsService.Current.ShortcutScreenshot = TbShortcutScreenshot.Text.Trim();
        _settingsService.Save();
    }

    private void OnExportSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (CmbExportFormat.SelectedItem is ComboBoxItem fItem && fItem.Content is string fmt)
        {
            _settingsService.Current.ExportFormat = fmt.ToLowerInvariant();
        }

        if (CmbExportResolution.SelectedItem is ComboBoxItem rItem && rItem.Content is string res)
        {
            _settingsService.Current.ExportResolution = res;
        }

        if (CmbExportFps.SelectedItem is ComboBoxItem fpsItem && fpsItem.Content is string fpsStr)
        {
            _settingsService.Current.Fps = fpsStr.StartsWith("30") ? 30 : 60;
        }

        _settingsService.Save();
    }

    #endregion
}
