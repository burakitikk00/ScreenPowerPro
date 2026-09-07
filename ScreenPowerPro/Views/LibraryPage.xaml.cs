using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenPowerPro.Models;
using ScreenPowerPro.ViewModels;

namespace ScreenPowerPro.Views;

/// <summary>
/// Kayıtlı projeleri listeleyen ve kullanıcıların düzenleme/silme/açma işlemlerini
/// yönettiği Kütüphane sayfası kod arkası (code-behind).
/// </summary>
public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; }

    public LibraryPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<LibraryViewModel>();
        DataContext = ViewModel;

        Loaded += OnPageLoaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.RefreshProjectsAsync();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateToEditor += OnNavigateToEditor;
        ViewModel.NavigateToDashboard += OnNavigateToDashboard;
    }

    private void OnNavigateToEditor(string projectPath)
    {
        MainWindow.CurrentInstance?.NavigateToEditor(projectPath);
    }

    private void OnNavigateToDashboard()
    {
        Frame.Navigate(typeof(DashboardPage));
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        OnNavigateToDashboard();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshProjectsAsync();
    }

    private void OnEditProjectClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProjectInfo project)
        {
            ViewModel.OpenProject(project);
        }
    }

    private void OnRevealClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProjectInfo project)
        {
            ViewModel.RevealInExplorer(project);
        }
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProjectInfo project)
        {
            ViewModel.DeleteProject(project);
        }
    }

    private void OnStartNewRecordingClicked(object sender, RoutedEventArgs e)
    {
        OnNavigateToDashboard();
    }
}
