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
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

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
        NavView.SelectedItem = NavDashboard;
        RootFrame.Navigate(typeof(DashboardPage));
    }

    public IntPtr GetWindowHandle()
    {
        return WinRT.Interop.WindowNative.GetWindowHandle(this);
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            RootFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItemContainer is NavigationViewItem item)
        {
            string? tag = item.Tag?.ToString();
            switch (tag)
            {
                case "Dashboard":
                    RootFrame.Navigate(typeof(DashboardPage));
                    break;
                case "Editor":
                    RootFrame.Navigate(typeof(EditorPage));
                    break;
                case "Export":
                    RootFrame.Navigate(typeof(ExportPage));
                    break;
            }
        }
    }

    public void NavigateToDashboard()
    {
        NavView.SelectedItem = NavDashboard;
        RootFrame.Navigate(typeof(DashboardPage));
    }

    public void NavigateToEditor(string projectDir)
    {
        NavView.SelectedItem = NavEditor;
        RootFrame.Navigate(typeof(EditorPage), projectDir);
    }

    public void NavigateToExport(string projectDir)
    {
        NavView.SelectedItem = NavExport;
        RootFrame.Navigate(typeof(ExportPage), projectDir);
    }
}
