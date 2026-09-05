using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScreenPowerPro.Services;
using ScreenPowerPro.ViewModels;

namespace ScreenPowerPro.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    private string _selectedMode = "FullScreen";

    public DashboardPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<DashboardViewModel>();
        DataContext = ViewModel;

        ViewModel.RequestStartRecording += OnRecordingStarted;
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        SelectModeCard("FullScreen");
        LoadRecentRecordings();
        LoadDeviceNames();
    }

    private void LoadDeviceNames()
    {
        TbMicName.Text = "Default Microphone";
        TbAudioName.Text = "Default";
        TbCameraName.Text = "No Camera";
    }

    private void LoadRecentRecordings()
    {
        ViewModel.RefreshRecentProjects();
        var projects = ViewModel.RecentProjects;

        if (projects == null || !projects.Any())
        {
            EmptyRecordingsState.Visibility = Visibility.Visible;
            RecentRecordingsList.ItemsSource = null;
        }
        else
        {
            EmptyRecordingsState.Visibility = Visibility.Collapsed;
            RecentRecordingsList.ItemsSource = projects.Take(3);
        }
    }

    private void OnModeSelected(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string mode)
        {
            SelectModeCard(mode);
            _selectedMode = mode;

            switch (mode)
            {
                case "FullScreen": ViewModel.SelectedMode = RecordingMode.FullScreen; break;
                case "CustomArea": ViewModel.SelectedMode = RecordingMode.Region; break;
                case "Window":
                    ViewModel.SelectedMode = RecordingMode.Window;
                    ViewModel.RefreshWindows();
                    break;
                case "Camera": ViewModel.SelectedMode = RecordingMode.FullScreen; break;
            }
        }
    }

    private void SelectModeCard(string mode)
    {
        // Reset all cards to default
        ResetCardStyle(BtnFullScreen);
        ResetCardStyle(BtnCustomArea);
        ResetCardStyle(BtnWindow);
        ResetCardStyle(BtnCamera);

        // Highlight selected
        var selected = mode switch
        {
            "FullScreen" => BtnFullScreen,
            "CustomArea" => BtnCustomArea,
            "Window" => BtnWindow,
            "Camera" => BtnCamera,
            _ => BtnFullScreen
        };

        selected.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(51, 192, 193, 255)); // primary/20
        selected.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 192, 193, 255)); // primary/50

        // Update icon color inside selected
        UpdateCardIconColor(selected, (Windows.UI.Color)App.Current.Resources["PrimaryColor"]);
    }

    private void ResetCardStyle(Button card)
    {
        card.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(13, 255, 255, 255)); // white/5
        card.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 255, 255, 255)); // white/10
        UpdateCardIconColor(card, (Windows.UI.Color)App.Current.Resources["TextSecondaryColor"]);
    }

    private void UpdateCardIconColor(Button card, Windows.UI.Color color)
    {
        if (card.Content is Border border &&
            border.Child is StackPanel sp)
        {
            foreach (var child in sp.Children)
            {
                if (child is Border iconBorder && iconBorder.Child is FontIcon icon)
                    icon.Foreground = new SolidColorBrush(color);
            }
        }
    }

    private void OnCameraDeviceClicked(object sender, RoutedEventArgs e)
    {
        // Device picker flyout — future implementation
    }

    private void OnMicDeviceClicked(object sender, RoutedEventArgs e)
    {
        // Mic device picker — future implementation
    }

    private void OnAudioDeviceClicked(object sender, RoutedEventArgs e)
    {
        // Audio device picker — future implementation
    }

    private async void OnStartRecording(object sender, RoutedEventArgs e)
    {
        ViewModel.IsHideCursorEnabled = TsHideCursor.IsOn;
        ViewModel.IsSystemAudioEnabled = TsRecordAudio.IsOn;
        await ViewModel.StartRecordingAsync();
    }

    private void OnRecordingStarted(string projectDir)
    {
        var recordingBar = new RecordingBarWindow(projectDir);
        recordingBar.Activate();

        var settingsService = App.Current.Services.GetRequiredService<SettingsService>();
        if (settingsService.Current.ExcludeAppFromRecording && MainWindow.CurrentInstance != null)
        {
            Helpers.Win32Helper.SetWindowDisplayAffinity(
                MainWindow.CurrentInstance.GetWindowHandle(),
                Helpers.Win32Helper.WDA_EXCLUDEFROMCAPTURE
            );
        }
    }

    private void OnViewAllClicked(object sender, RoutedEventArgs e)
    {
        // MainWindow.CurrentInstance?.NavigateToLibrary();
    }

    private void OnOpenRecentRecording(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string projectDir)
        {
            MainWindow.CurrentInstance?.NavigateToEditor(projectDir);
        }
    }
}
