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
    private readonly LocalizationService _locService;
    private bool _isInitializing = true;

    public SettingsWindow()
    {
        InitializeComponent();

        _settingsService = App.Current.Services.GetRequiredService<SettingsService>();
        _locService = App.Current.Services.GetRequiredService<LocalizationService>();

        _locService.LanguageChanged += OnLanguageChanged;
        Closed += (s, e) => _locService.LanguageChanged -= OnLanguageChanged;

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

        ApplyLocalization();
        LoadSettingsToUI();
    }

    private void OnLanguageChanged()
    {
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        Title = _locService["Settings_Title"];
        if (TbSettingsTitle != null) TbSettingsTitle.Text = _locService["Settings_Title"];
        if (TbTabGeneral != null) TbTabGeneral.Text = _locService["Settings_Tab_General"];
        if (TbTabRecord != null) TbTabRecord.Text = _locService["Settings_Tab_Record"];
        if (TbTabShortcuts != null) TbTabShortcuts.Text = _locService["Settings_Tab_Shortcuts"];
        if (TbTabExport != null) TbTabExport.Text = _locService["Settings_Tab_Export"];

        if (TbLanguageLabel != null) TbLanguageLabel.Text = _locService["Settings_Language"];
        if (TbSaveLocationLabel != null) TbSaveLocationLabel.Text = _locService["Settings_SaveLocation"];
        if (TbExportLocationLabel != null) TbExportLocationLabel.Text = _locService["Settings_ExportLocation"];
        if (TbAutoStartLabel != null) TbAutoStartLabel.Text = _locService["Settings_AutoStart"];
        if (TbAutoPlayVideoLabel != null) TbAutoPlayVideoLabel.Text = _locService["Settings_AutoPlay"];

        if (TbZoomEffectLabel != null) TbZoomEffectLabel.Text = _locService["Settings_ZoomEffect"];
        if (TbHideDesktopIconsLabel != null) TbHideDesktopIconsLabel.Text = _locService["Settings_HideDesktopIcons"];
        if (TbHideTaskbarLabel != null) TbHideTaskbarLabel.Text = _locService["Settings_HideTaskbar"];
        if (TbRecordingQualityLabel != null) TbRecordingQualityLabel.Text = _locService["Settings_Quality"];
        if (TbCountdownLabel != null) TbCountdownLabel.Text = _locService["Settings_Countdown"];

        if (TbShortcutStartStopLabel != null) TbShortcutStartStopLabel.Text = _locService["Settings_Shortcut_StartStop"];
        if (TbShortcutPauseLabel != null) TbShortcutPauseLabel.Text = _locService["Settings_Shortcut_Pause"];
        if (TbShortcutScreenshotLabel != null) TbShortcutScreenshotLabel.Text = _locService["Settings_Shortcut_Screenshot"];

        if (TbExportFormatLabel != null) TbExportFormatLabel.Text = _locService["Settings_ExportFormat"];
        if (TbExportResolutionLabel != null) TbExportResolutionLabel.Text = _locService["Settings_ExportResolution"];
        if (TbExportFpsLabel != null) TbExportFpsLabel.Text = _locService["Settings_Fps"];
        if (TbRecordingResolutionLabel != null) TbRecordingResolutionLabel.Text = _locService["Settings_RecordingResolution"] ?? "Target Recording Resolution";
    }

    private void LoadSettingsToUI()
    {
        _isInitializing = true;
        var s = _settingsService.Current;

        // General
        CmbLanguage.SelectedIndex = string.Equals(_locService.CurrentLanguage, "en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
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

        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea != null && displayArea.OuterBounds.Width < 3840)
        {
            ComboItemRecord4K.Visibility = Visibility.Collapsed;
        }
        else
        {
            ComboItemRecord4K.Visibility = Visibility.Visible;
        }

        SetComboSelection(CmbRecordingResolution, s.Resolution ?? "Original");

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

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        string targetLang = CmbLanguage.SelectedIndex == 1 ? "en" : "tr";
        _locService.SetLanguage(targetLang);
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
    }

    private void OnHideTaskbarToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        bool isOn = SwitchHideTaskbar.IsOn;
        _settingsService.Current.HideTaskbar = isOn;
        _settingsService.Save();
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

    private void OnRecordingResolutionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CmbRecordingResolution.SelectedItem is ComboBoxItem item && item.Content is string val)
        {
            _settingsService.Current.Resolution = val;
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
