using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenPowerPro.Views;
using Windows.Graphics;

namespace ScreenPowerPro;

public sealed partial class MainWindow : Window
{
    public static MainWindow? CurrentInstance { get; private set; }

    public MainWindow()
    {
        CurrentInstance = this;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Custom Titlebar colors
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(25, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(50, 255, 255, 255);
            titleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
        }

        // Set initial window size (1180 x 820)
        AppWindow.Resize(new SizeInt32(1180, 820));

        // Center on screen
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            int x = (displayArea.WorkArea.Width - 1180) / 2;
            int y = (displayArea.WorkArea.Height - 820) / 2;
            AppWindow.Move(new PointInt32(Math.Max(0, x), Math.Max(0, y)));
        }

        // Navigate to Dashboard initially
        RootFrame.Navigate(typeof(DashboardPage));
    }

    public IntPtr GetWindowHandle()
    {
        return WinRT.Interop.WindowNative.GetWindowHandle(this);
    }

    public void NavigateToDashboard()
    {
        RootFrame.Navigate(typeof(DashboardPage));
    }

    public void NavigateToEditor(string projectDir)
    {
        RootFrame.Navigate(typeof(EditorPage), projectDir);
    }

    public void NavigateToExport(string projectDir)
    {
        RootFrame.Navigate(typeof(ExportPage), projectDir);
    }
}
