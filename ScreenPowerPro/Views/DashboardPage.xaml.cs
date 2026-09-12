using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using ScreenPowerPro.Models;
using ScreenPowerPro.Services;
using ScreenPowerPro.ViewModels;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace ScreenPowerPro.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }
    private readonly AudioLevelMonitorService _audioMonitor;
    private readonly LocalizationService _loc;

    private string _activeMode = "FullScreen";

    public DashboardPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<DashboardViewModel>();
        _audioMonitor = App.Current.Services.GetRequiredService<AudioLevelMonitorService>();
        _loc = App.Current.Services.GetRequiredService<LocalizationService>();
        DataContext = ViewModel;

        ViewModel.RequestStartRecording += OnRecordingStarted;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        MainWindow.CurrentInstance?.SetTitleBar(TitleBarDragArea);
        UpdateTitleBarLayout();
        if (XamlRoot != null)
        {
            XamlRoot.Changed += OnXamlRootChanged;
        }

        _loc.LanguageChanged += ApplyLocalization;
        ApplyLocalization();

        SelectMode("FullScreen");
        await ViewModel.InitializeDevicesAsync();
        UpdateOnlyAppMenuText();

        _audioMonitor.AudioLevelsChanged += OnAudioLevelsChanged;
        _audioMonitor.StartMonitoring();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.RequestStartRecording -= OnRecordingStarted;
        _loc.LanguageChanged -= ApplyLocalization;
        if (XamlRoot != null)
        {
            XamlRoot.Changed -= OnXamlRootChanged;
        }
        _audioMonitor.AudioLevelsChanged -= OnAudioLevelsChanged;
        _audioMonitor.StopMonitoring();
    }

    private void ApplyLocalization()
    {
        if (MenuFileHistory != null) MenuFileHistory.Text = _loc["Dashboard_Header_History"];
        if (MenuFileImport != null) MenuFileImport.Text = _loc["Dashboard_History_ImportVideo"];
        if (TbFileBtn != null) TbFileBtn.Text = _loc.CurrentLanguage == "en" ? "File" : "Dosya";
        if (BtnHeaderSettings != null) ToolTipService.SetToolTip(BtnHeaderSettings, _loc["Dashboard_Header_Settings"]);

        if (TbSelectModeTitle != null) TbSelectModeTitle.Text = _loc["Dashboard_Subtitle"];
        if (TbDeviceToolTitle != null) TbDeviceToolTitle.Text = _loc.CurrentLanguage == "en" ? "Device & Tool" : "Cihazlar & Araçlar";

        if (TbModeFullScreenTitle != null) TbModeFullScreenTitle.Text = _loc["Dashboard_Mode_Screen"];
        if (TbModeCustomTitle != null) TbModeCustomTitle.Text = _loc["Dashboard_Mode_Custom"];
        if (TbModeWindowTitle != null) TbModeWindowTitle.Text = _loc["Dashboard_Mode_Window"];
        if (TbModeDeviceTitle != null) TbModeDeviceTitle.Text = _loc["Dashboard_Mode_Camera"];

        if (TbTeleprompterTool != null) TbTeleprompterTool.Text = _loc["Dashboard_Header_Teleprompter"];
        if (TbSelectCameraTitle != null) TbSelectCameraTitle.Text = _loc.CurrentLanguage == "en" ? "Select Camera" : "Kamera Seçin";
        if (TbSelectMicTitle != null) TbSelectMicTitle.Text = _loc.CurrentLanguage == "en" ? "Select Microphone" : "Mikrofon Seçin";
        if (TbSelectAudioAppTitle != null) TbSelectAudioAppTitle.Text = _loc.CurrentLanguage == "en" ? "Select Running Applications" : "Çalışan Uygulamaları Seçin";

        if (TbHistoryTitle != null) TbHistoryTitle.Text = _loc["Dashboard_History_Title"];
        if (TbTabProjectHistory != null) TbTabProjectHistory.Text = _loc["Dashboard_History_TabRecordings"];
        if (TbTabSharingHistory != null) TbTabSharingHistory.Text = _loc["Dashboard_History_TabShared"];
        if (TbHistoryEmpty != null) TbHistoryEmpty.Text = _loc["Dashboard_History_Empty"];

        UpdateOnlyAppMenuText();
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        UpdateTitleBarLayout();
    }

    private void UpdateTitleBarLayout()
    {
        if (MainWindow.CurrentInstance != null && AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = MainWindow.CurrentInstance.AppWindow.TitleBar;
            double scale = XamlRoot?.RasterizationScale ?? 1.0;
            double rightInset = titleBar.RightInset > 0 ? (titleBar.RightInset / scale) : 96.0;
            CaptionSpacerColumn.Width = new GridLength(Math.Max(rightInset, 90.0));
        }
    }

    private void OnAudioLevelsChanged(float micLevel, float speakerLevel)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // Mikrofon alttan yukarıya sese duyarlı dolgu ve pill göstergesi
            MicAudioLevelFill.Height = Math.Clamp(micLevel * 30.0, 0.0, 30.0);
            MicAudioPillBar.Height = Math.Clamp(micLevel * 14.0, 0.0, 14.0);

            // Hoparlör alttan yukarıya sese duyarlı dolgu ve pill göstergesi
            SpeakerAudioLevelFill.Height = Math.Clamp(speakerLevel * 30.0, 0.0, 30.0);
            SpeakerAudioPillBar.Height = Math.Clamp(speakerLevel * 14.0, 0.0, 14.0);
        });
    }

    #region Recording Mode Cards

    private void OnModeCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            // Mavi hover çerçevesi
            card.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 59, 130, 246)); // #3B82F6 Bright Blue
            card.BorderThickness = new Thickness(2);
        }
    }

    private void OnModeCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card && card.Tag is string tag)
        {
            // Eğer aktif seçili mod değilse pasif kenarlığa geri dön
            if (tag != _activeMode)
            {
                ResetCardBorder(card);
            }
            else
            {
                card.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 37, 99, 235)); // #2563EB Active Blue
                card.BorderThickness = new Thickness(2);
            }
        }
    }

    private async void OnModeCardClicked(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card && card.Tag is string mode)
        {
            SelectMode(mode);
            await LaunchRecordingSetupModeAsync(mode);
        }
    }

    private async Task LaunchRecordingSetupModeAsync(string mode)
    {
        // 1. Dashboard penceresini simge durumuna küçült / gizle
        if (MainWindow.CurrentInstance != null)
        {
            var appWin = MainWindow.CurrentInstance.AppWindow;
            if (appWin.Presenter is OverlappedPresenter presenter)
            {
                presenter.Minimize();
            }
        }

        if (mode == "CustomArea")
        {
            // Bölge seçim penceresini aç
            var regionWindow = new RegionSelectionWindow();
            regionWindow.Activate();
            var rect = await regionWindow.WaitForSelectionAsync();

            if (rect.HasValue)
            {
                var r = rect.Value;
                ViewModel.SelectedMode = RecordingMode.Region;
                ViewModel.LastCropX = (int)r.X;
                ViewModel.LastCropY = (int)r.Y;
                ViewModel.LastCropWidth = (int)r.Width;
                ViewModel.LastCropHeight = (int)r.Height;

                // Seçilen özel bölgenin etrafında kesikli mavi çerçeve göster
                var borderWin = new FullScreenBorderWindow(new RectInt32((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height));
                borderWin.Activate();

                // 2. Sayfa: Alt yüzen kurulum araç çubuğu
                var toolbarWin = new RecordingSetupToolbarWindow();
                toolbarWin.Activate();
            }
            else
            {
                // Kullanıcı iptal etti, Dashboard'u geri getir
                RestoreMainWindow();
            }
        }
        else
        {
            // FullScreen / Window / Device
            if (mode == "FullScreen" || mode == "Device")
            {
                // Tam ekran çerçevesini kesikli çizgilerle göster
                var borderWin = new FullScreenBorderWindow();
                borderWin.Activate();
            }

            // 2. Sayfa: Alt yüzen kurulum araç çubuğunu aç
            var toolbarWin = new RecordingSetupToolbarWindow();
            toolbarWin.Activate();
        }
    }

    private void RestoreMainWindow()
    {
        if (MainWindow.CurrentInstance != null)
        {
            var appWin = MainWindow.CurrentInstance.AppWindow;
            if (appWin.Presenter is OverlappedPresenter presenter)
            {
                presenter.Restore();
            }
            MainWindow.CurrentInstance.Activate();
        }
    }

    private void SelectMode(string mode)
    {
        _activeMode = mode;

        // Reset all card borders to inactive
        ResetCardBorder(BorderCardFullScreen);
        ResetCardBorder(BorderCardCustom);
        ResetCardBorder(BorderCardWindow);
        ResetCardBorder(BorderCardDevice);

        // Highlight selected
        Border selectedCard = mode switch
        {
            "FullScreen" => BorderCardFullScreen,
            "CustomArea" => BorderCardCustom,
            "Window" => BorderCardWindow,
            "Device" => BorderCardDevice,
            _ => BorderCardFullScreen
        };

        selectedCard.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 37, 99, 235)); // #2563EB Vivid Blue
        selectedCard.BorderThickness = new Thickness(2);

        // Update ViewModel Mode
        switch (mode)
        {
            case "FullScreen":
                ViewModel.SelectedMode = RecordingMode.FullScreen;
                break;
            case "CustomArea":
                ViewModel.SelectedMode = RecordingMode.Region;
                break;
            case "Window":
                ViewModel.SelectedMode = RecordingMode.Window;
                ViewModel.RefreshWindows();
                break;
            case "Device":
                ViewModel.SelectedMode = RecordingMode.FullScreen;
                break;
        }
    }

    private void ResetCardBorder(Border card)
    {
        card.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 31, 32, 43)); // #1F202B
        card.BorderThickness = new Thickness(1);
    }

    #endregion

    #region Device Dropdowns & Selection

    private void OnCameraDropdownClicked(object sender, RoutedEventArgs e)
    {
        CameraItemsList.ItemsSource = ViewModel.DeviceManager.Cameras;
        CameraPopupOverlay.Visibility = Visibility.Visible;
        MicPopupOverlay.Visibility = Visibility.Collapsed;
        SpeakerPopupOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnSelectCameraItem(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DeviceItem item)
        {
            ViewModel.DeviceManager.SelectCamera(item.Id);
            CameraPopupOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void OnMicDropdownClicked(object sender, RoutedEventArgs e)
    {
        MicItemsList.ItemsSource = ViewModel.DeviceManager.Microphones;
        MicPopupOverlay.Visibility = Visibility.Visible;
        CameraPopupOverlay.Visibility = Visibility.Collapsed;
        SpeakerPopupOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnSelectMicItem(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DeviceItem item)
        {
            ViewModel.DeviceManager.SelectMicrophone(item.Id);
            MicPopupOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void OnSpeakerDropdownClicked(object sender, RoutedEventArgs e)
    {
        SpeakerItemsList.ItemsSource = ViewModel.DeviceManager.Speakers;
        AudioAppItemsList.ItemsSource = ViewModel.DeviceManager.OpenAudioApps;
        UpdateOnlyAppMenuText();

        SpeakerPopupOverlay.Visibility = Visibility.Visible;
        CameraPopupOverlay.Visibility = Visibility.Collapsed;
        MicPopupOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnSelectSpeakerItem(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DeviceItem item)
        {
            ViewModel.DeviceManager.SelectSpeaker(item.Id);
            SpeakerPopupOverlay.Visibility = Visibility.Collapsed;
            OnlyAppSubFlyout.Visibility = Visibility.Collapsed;
        }
    }

    private void OnToggleOnlyAppSubFlyout(object sender, RoutedEventArgs e)
    {
        ViewModel.DeviceManager.RefreshOpenAudioApps();
        AudioAppItemsList.ItemsSource = ViewModel.DeviceManager.OpenAudioApps;

        OnlyAppSubFlyout.Visibility = OnlyAppSubFlyout.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void OnAppAudioCheckChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is AudioAppItem app)
        {
            ViewModel.DeviceManager.ToggleAppAudioSelection(app, cb.IsChecked == true);
            UpdateOnlyAppMenuText();
        }
    }

    private void UpdateOnlyAppMenuText()
    {
        int count = ViewModel.DeviceManager.GetSelectedAppCount();
        TbOnlyAppMenuText.Text = $"Only App Audio ({count})";
    }

    private void OnClosePopupOverlay(object sender, PointerRoutedEventArgs e)
    {
        CameraPopupOverlay.Visibility = Visibility.Collapsed;
        MicPopupOverlay.Visibility = Visibility.Collapsed;
        SpeakerPopupOverlay.Visibility = Visibility.Collapsed;
        OnlyAppSubFlyout.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Teleprompter

    private void OnTeleprompterClicked(object sender, RoutedEventArgs e)
    {
        var teleprompter = new TeleprompterWindow();
        teleprompter.Activate();
    }

    #endregion

    #region File Menu & History Modal

    private void OnFileHistoryClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshRecentProjects();
        RefreshHistoryProjectsList();
        FileHistoryModalOverlay.Visibility = Visibility.Visible;
    }

    private void OnCloseFileHistory(object sender, RoutedEventArgs e)
    {
        FileHistoryModalOverlay.Visibility = Visibility.Collapsed;
    }

    private void RefreshHistoryProjectsList()
    {
        var projects = ViewModel.RecentProjects;
        if (projects == null || !projects.Any())
        {
            TbHistoryEmpty.Visibility = Visibility.Visible;
            HistoryProjectsList.ItemsSource = null;
        }
        else
        {
            TbHistoryEmpty.Visibility = Visibility.Collapsed;
            HistoryProjectsList.ItemsSource = projects;
        }
    }

    private void OnTabProjectHistoryClicked(object sender, RoutedEventArgs e)
    {
        BtnTabProjectHistory.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 30, 58, 138));
        BtnTabProjectHistory.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 59, 130, 246));
        BtnTabProjectHistory.BorderThickness = new Thickness(1);

        BtnTabSharingHistory.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 26, 27, 36));
        BtnTabSharingHistory.BorderThickness = new Thickness(0);

        TbHistoryEmpty.Text = _loc["Dashboard_History_Empty"];
        RefreshHistoryProjectsList();
    }

    private void OnTabSharingHistoryClicked(object sender, RoutedEventArgs e)
    {
        BtnTabSharingHistory.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 30, 58, 138));
        BtnTabSharingHistory.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 59, 130, 246));
        BtnTabSharingHistory.BorderThickness = new Thickness(1);

        BtnTabProjectHistory.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 26, 27, 36));
        BtnTabProjectHistory.BorderThickness = new Thickness(0);

        // Sharing history empty placeholder
        TbHistoryEmpty.Text = _loc["Dashboard_History_SharedEmpty"];
        TbHistoryEmpty.Visibility = Visibility.Visible;
        HistoryProjectsList.ItemsSource = null;
    }

    private void OnOpenProjectFromHistory(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProjectInfo project)
        {
            FileHistoryModalOverlay.Visibility = Visibility.Collapsed;
            MainWindow.CurrentInstance?.NavigateToEditor(project.FolderPath);
        }
    }

    private async void OnRenameProjectClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProjectInfo project)
        {
            var textBox = new TextBox
            {
                Text = project.Name,
                PlaceholderText = _loc["Dashboard_Rename_Placeholder"],
                Margin = new Thickness(0, 8, 0, 0)
            };

            var dialog = new ContentDialog
            {
                Title = _loc["Dashboard_Rename_Title"],
                Content = textBox,
                PrimaryButtonText = _loc["Common_Save"],
                CloseButtonText = _loc["Common_Cancel"],
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                ViewModel.RenameProject(project.FolderPath, textBox.Text.Trim());
                RefreshHistoryProjectsList();
            }
        }
    }

    private void OnRevealProjectClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProjectInfo project)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{project.FolderPath}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private async void OnDeleteProjectClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProjectInfo project)
        {
            var dialog = new ContentDialog
            {
                Title = _loc["Dashboard_Delete_Title"],
                Content = _loc.Get("Dashboard_Delete_Confirm", project.Name),
                PrimaryButtonText = _loc["Common_Delete"],
                CloseButtonText = _loc["Common_Cancel"],
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                ViewModel.DeleteProject(project.FolderPath);
                RefreshHistoryProjectsList();
            }
        }
    }

    private async void OnImportVideoClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            picker.FileTypeFilter.Add(".mp4");
            picker.FileTypeFilter.Add(".mov");
            picker.FileTypeFilter.Add(".mkv");
            picker.FileTypeFilter.Add(".avi");
            picker.FileTypeFilter.Add(".webm");

            IntPtr hwnd = MainWindow.CurrentInstance?.GetWindowHandle() ?? IntPtr.Zero;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                string projectDir = ViewModel.ImportVideo(file.Path);
                MainWindow.CurrentInstance?.NavigateToEditor(projectDir);
            }
        }
        catch { }
    }

    #endregion

    #region Navigation & Recording

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        var settingsWin = new SettingsWindow();
        settingsWin.Activate();
    }

    private void OnStartRecordingClicked(object sender, RoutedEventArgs e)
    {
        var settingsService = App.Current.Services.GetRequiredService<SettingsService>();
        int countdown = settingsService.Current.CountdownSeconds;

        if (countdown > 0)
        {
            if (MainWindow.CurrentInstance != null)
            {
                var appWin = MainWindow.CurrentInstance.AppWindow;
                if (appWin.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.Minimize();
                }
            }

            var countdownWindow = new CountdownWindow(countdown, () =>
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await ViewModel.StartRecordingAsync();
                });
            });
            countdownWindow.Activate();
        }
        else
        {
            if (MainWindow.CurrentInstance != null)
            {
                var appWin = MainWindow.CurrentInstance.AppWindow;
                if (appWin.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.Minimize();
                }
            }
            _ = ViewModel.StartRecordingAsync();
        }
    }

    private void OnRecordingStarted(string projectDir)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // 1. Floating Recording Bar
            int recW = ViewModel.SelectedMode == RecordingMode.Region && ViewModel.LastCropWidth > 0 ? ViewModel.LastCropWidth : 1920;
            int recH = ViewModel.SelectedMode == RecordingMode.Region && ViewModel.LastCropHeight > 0 ? ViewModel.LastCropHeight : 1080;
            int origX = ViewModel.SelectedMode == RecordingMode.Region ? ViewModel.LastCropX : 0;
            int origY = ViewModel.SelectedMode == RecordingMode.Region ? ViewModel.LastCropY : 0;

            var recordingBar = new RecordingBarWindow(projectDir, recW, recH, origX, origY);
            recordingBar.Activate();

            var settingsService = App.Current.Services.GetRequiredService<SettingsService>();

            // 2. Camera overlay if enabled and not "none"
            if (settingsService.Current.CameraEnabled &&
                !string.IsNullOrEmpty(settingsService.Current.SelectedCameraDevice) &&
                settingsService.Current.SelectedCameraDevice != "none")
            {
                var cameraOverlay = new CameraOverlayWindow();
                cameraOverlay.Activate();
            }

            // 3. Mask overlay if region recording
            if (ViewModel.SelectedMode == RecordingMode.Region && ViewModel.LastCropWidth > 0 && ViewModel.LastCropHeight > 0)
            {
                var maskOverlay = new MaskOverlayWindow(
                    ViewModel.LastCropX,
                    ViewModel.LastCropY,
                    ViewModel.LastCropWidth,
                    ViewModel.LastCropHeight);
                maskOverlay.Activate();
            }

            // 4. Window exclusion from capture
            if (settingsService.Current.ExcludeAppFromRecording && MainWindow.CurrentInstance != null)
            {
                Helpers.Win32Helper.SetWindowDisplayAffinity(
                    MainWindow.CurrentInstance.GetWindowHandle(),
                    Helpers.Win32Helper.WDA_EXCLUDEFROMCAPTURE
                );
            }
        });
    }

    #endregion
}
