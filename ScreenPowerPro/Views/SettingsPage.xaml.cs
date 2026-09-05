using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenPowerPro.ViewModels;

namespace ScreenPowerPro.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<SettingsViewModel>();
        DataContext = ViewModel;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.Save();
        SaveSuccessInfoBar.IsOpen = true;
    }
}
