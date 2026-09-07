using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenPowerPro.Services;
using ScreenPowerPro.ViewModels;

namespace ScreenPowerPro.Views;

public sealed partial class ExportPage : Page
{
    public ExportViewModel ViewModel { get; }

    private const double RingCircumference = 729.0;
    private string? _outputFolder;
    private readonly LocalizationService _loc;

    public ExportPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<ExportViewModel>();
        _loc = App.Current.Services.GetRequiredService<LocalizationService>();
        DataContext = ViewModel;
        
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += (s, e) =>
        {
            _loc.LanguageChanged += ApplyLocalization;
            ApplyLocalization();
        };
        Unloaded += (s, e) =>
        {
            _loc.LanguageChanged -= ApplyLocalization;
        };
    }

    private void ApplyLocalization()
    {
        if (ViewModel.IsCompleted)
        {
            TbStatusTitle.Text = _loc["Export_SuccessTitle"];
            TbEstimatedTime.Text = _loc["Export_SuccessDesc"];
        }
        else
        {
            TbStatusTitle.Text = _loc["Export_Title"];
            TbEstimatedTime.Text = _loc["Export_Calculating"];
        }
        TbEstimatedLabel.Text = _loc["Export_Desc"];
        TbFormatLabel.Text = _loc["Format"].ToUpperInvariant();
        TbResLabel.Text = _loc["Resolution"].ToUpperInvariant();
        TbFpsLabel.Text = _loc["Fps"].ToUpperInvariant();
        TbBtnCancelText.Text = _loc["Export_Cancel"];
        TbDoneTitle.Text = _loc["Export_SuccessTitle"];
        TbBtnOpenFolderText.Text = _loc["Export_OpenFolder"];
        TbBtnBackToEditorText.Text = _loc["Export_BackToEditor"];
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName == nameof(ViewModel.ProgressPercent))
            {
                int pct = (int)ViewModel.ProgressPercent;
                TbPercentage.Text = $"{pct}%";

                double offset = RingCircumference * (1.0 - (ViewModel.ProgressPercent / 100.0));
                ProgressRingArc.StrokeDashOffset = offset;
            }
            else if (e.PropertyName == nameof(ViewModel.StatusMessage))
            {
                TbOperation.Text = ViewModel.StatusMessage;
            }
            else if (e.PropertyName == nameof(ViewModel.IsCompleted))
            {
                if (ViewModel.IsCompleted)
                {
                    _outputFolder = Path.GetDirectoryName(ViewModel.OutputPath);
                    TbPercentage.Text = "100%";
                    ProgressRingArc.StrokeDashOffset = 0;

                    TbStatusTitle.Text = _loc["Export_SuccessTitle"];
                    TbEstimatedTime.Text = _loc["Export_SuccessDesc"];
                    BtnCancel.Visibility = Visibility.Collapsed;
                    DoneState.Visibility = Visibility.Visible;
                }
            }
        });
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string projectDir)
        {
            ViewModel.LoadProject(projectDir);
            await ViewModel.StartExportAsync();
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelExport();
        MainWindow.CurrentInstance?.NavigateToEditor(ViewModel.ProjectDir);
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenOutputFolder();
    }

    private void OnBackToEditorClicked(object sender, RoutedEventArgs e)
    {
        MainWindow.CurrentInstance?.NavigateToEditor(ViewModel.ProjectDir);
    }
}
