using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenPowerPro.ViewModels;

namespace ScreenPowerPro.Views;

/// <summary>
/// Uygulama ayarları (genel dizinler, kayıt özellikleri, çıktı kalitesi ve donanım aygıtları)
/// sayfasının kod arkası (code-behind).
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<SettingsViewModel>();
        DataContext = ViewModel;
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        if (MainWindow.CurrentInstance != null)
        {
            MainWindow.CurrentInstance.NavigateToDashboard();
        }
        else if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(typeof(DashboardPage));
        }
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.Save();
        SaveSuccessInfoBar.IsOpen = true;
    }
}
