using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenPowerPro.ViewModels;

namespace ScreenPowerPro.Views;

public sealed partial class ExportPage : Page
{
    public ExportViewModel ViewModel { get; }

    private const double RingCircumference = 729.0;
    private string? _outputFolder;

    public ExportPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<ExportViewModel>();
        DataContext = ViewModel;
        
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
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

                    TbStatusTitle.Text = "Export Tamamlandı!";
                    TbEstimatedTime.Text = "Başarıyla kaydedildi";
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
