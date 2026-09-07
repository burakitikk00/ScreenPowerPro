using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenPowerPro.Models;
using ScreenPowerPro.Services;
using ScreenPowerPro.ViewModels;

namespace ScreenPowerPro.Views;

/// <summary>
/// Kayıtlı projeleri listeleyen ve kullanıcıların düzenleme/silme/açma işlemlerini
/// yönettiği Kütüphane sayfası kod arkası (code-behind).
/// </summary>
public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; }
    private readonly LocalizationService _loc;

    public LibraryPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<LibraryViewModel>();
        _loc = App.Current.Services.GetRequiredService<LocalizationService>();
        DataContext = ViewModel;

        Loaded += OnPageLoaded;
        Unloaded += (s, e) =>
        {
            _loc.LanguageChanged -= ApplyLocalization;
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.RefreshProjectsAsync();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _loc.LanguageChanged += ApplyLocalization;
        ApplyLocalization();

        ViewModel.NavigateToEditor += OnNavigateToEditor;
        ViewModel.NavigateToDashboard += OnNavigateToDashboard;
    }

    private void ApplyLocalization()
    {
        if (TbNavBack != null) TbNavBack.Text = _loc["Library_Nav_Dashboard"];
        if (TbNavTitle != null) TbNavTitle.Text = _loc["Library_Nav_Title"];
        if (TbBtnRefresh != null) TbBtnRefresh.Text = _loc["Library_Refresh"];
        if (TbHeaderTitle != null) TbHeaderTitle.Text = _loc["Library_Title"];
        if (TbHeaderDesc != null) TbHeaderDesc.Text = _loc["Library_Desc"];
        if (TbEmptyTitle != null) TbEmptyTitle.Text = _loc["Library_Empty_Title"];
        if (TbEmptyDesc != null) TbEmptyDesc.Text = _loc["Library_Empty_Desc"];
        if (TbBtnStartFirst != null) TbBtnStartFirst.Text = _loc.CurrentLanguage == "en" ? "Start First Recording" : "İlk Kaydı Başlat";
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
