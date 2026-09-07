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

    private const double RingCircumference = 72.885;
    private double _displayProgress = 0.0;
    private double _targetProgress = 0.0;
    private DispatcherTimer? _smoothProgressTimer;
    private string? _outputFolder;
    private readonly LocalizationService _loc;

    public ExportPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<ExportViewModel>();
        _loc = App.Current.Services.GetRequiredService<LocalizationService>();
        DataContext = ViewModel;

        _smoothProgressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(25)
        };
        _smoothProgressTimer.Tick += OnSmoothProgressTick;
        
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += (s, e) =>
        {
            _smoothProgressTimer.Start();
            _loc.LanguageChanged += ApplyLocalization;
            ApplyLocalization();
        };
        Unloaded += (s, e) =>
        {
            _smoothProgressTimer.Stop();
            _loc.LanguageChanged -= ApplyLocalization;
        };
    }

    private void OnSmoothProgressTick(object? sender, object e)
    {
        if (Math.Abs(_displayProgress - _targetProgress) > 0.05)
        {
            // Yumuşak yaklaşım (lerp)
            _displayProgress += (_targetProgress - _displayProgress) * 0.18;
            if (_targetProgress >= 100.0 && _displayProgress > 99.5)
            {
                _displayProgress = 100.0;
            }
        }
        else
        {
            _displayProgress = _targetProgress;
        }

        int pct = (int)Math.Round(_displayProgress);
        TbPercentage.Text = $"{pct}%";

        double offset = RingCircumference * (1.0 - (Math.Clamp(_displayProgress, 0.0, 100.0) / 100.0));
        ProgressRingArc.StrokeDashOffset = offset;
    }

    private void ApplyLocalization()
    {
        if (ViewModel.HasError)
        {
            TbStatusTitle.Text = _loc["Export_FailedTitle"];
            TbEstimatedTime.Text = "Hata oluştu";
        }
        else if (ViewModel.IsCompleted)
        {
            TbStatusTitle.Text = _loc["Export_SuccessTitle"];
            TbEstimatedTime.Text = _loc["Export_SuccessDesc"];
        }
        else
        {
            TbStatusTitle.Text = _loc["Export_Title"];
            TbEstimatedTime.Text = ViewModel.EstimatedTimeRemaining;
        }
        TbEstimatedLabel.Text = _loc["Export_Desc"];
        TbFormatLabel.Text = _loc["Format"].ToUpperInvariant();
        TbResLabel.Text = _loc["Resolution"].ToUpperInvariant();
        TbFpsLabel.Text = _loc["Fps"].ToUpperInvariant();
        TbBtnCancelText.Text = _loc["Export_Cancel"];
        TbDoneTitle.Text = _loc["Export_SuccessTitle"];
        TbBtnOpenFolderText.Text = _loc["Export_OpenFolder"];
        TbBtnBackToEditorText.Text = _loc["Export_BackToEditor"];
        if (TbErrorTitle != null) TbErrorTitle.Text = _loc["Export_FailedTitle"];
        if (TbBtnBackErrorText != null) TbBtnBackErrorText.Text = _loc["Export_BackToEditor"];
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName == nameof(ViewModel.ProgressPercent))
            {
                _targetProgress = Math.Clamp(ViewModel.ProgressPercent, 0.0, 100.0);
            }
            else if (e.PropertyName == nameof(ViewModel.EstimatedTimeRemaining))
            {
                if (!ViewModel.IsCompleted && !ViewModel.HasError)
                {
                    TbEstimatedTime.Text = ViewModel.EstimatedTimeRemaining;
                }
            }
            else if (e.PropertyName == nameof(ViewModel.StatusMessage))
            {
                TbOperation.Text = ViewModel.StatusMessage;
            }
            else if (e.PropertyName == nameof(ViewModel.HasError))
            {
                if (ViewModel.HasError)
                {
                    _smoothProgressTimer?.Stop();
                    TbStatusTitle.Text = _loc["Export_FailedTitle"];
                    TbEstimatedTime.Text = "Hata oluştu";
                    TbOperation.Text = ViewModel.ErrorMessage;
                    TbPercentage.Text = "!";
                    BtnCancel.Visibility = Visibility.Collapsed;
                    DoneState.Visibility = Visibility.Collapsed;
                    ErrorState.Visibility = Visibility.Visible;
                }
                else
                {
                    ErrorState.Visibility = Visibility.Collapsed;
                }
            }
            else if (e.PropertyName == nameof(ViewModel.IsCompleted))
            {
                if (ViewModel.IsCompleted)
                {
                    _targetProgress = 100.0;
                    _displayProgress = 100.0;
                    _outputFolder = Path.GetDirectoryName(ViewModel.OutputPath);
                    TbPercentage.Text = "100%";
                    ProgressRingArc.StrokeDashOffset = 0;

                    TbStatusTitle.Text = _loc["Export_SuccessTitle"];
                    TbEstimatedTime.Text = _loc["Export_SuccessDesc"];
                    BtnCancel.Visibility = Visibility.Collapsed;
                    ErrorState.Visibility = Visibility.Collapsed;
                    DoneState.Visibility = Visibility.Visible;
                }
            }
        });
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ScreenPowerPro.Models.ExportOptions options)
        {
            ViewModel.LoadProjectWithOptions(options);
            TbResolution.Text = $"{options.TargetWidth}×{options.TargetHeight}";
            TbFps.Text = options.TargetFps.ToString();
            TbFormat.Text = (options.Format ?? "MP4").ToUpperInvariant() + " (H.264)";
            await ViewModel.StartExportAsync();
        }
        else if (e.Parameter is string projectDir)
        {
            ViewModel.LoadProject(projectDir);
            TbResolution.Text = "1920×1080";
            TbFps.Text = "60";
            TbFormat.Text = "MP4 (H.264)";
            await ViewModel.StartExportAsync();
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        string pDir = ViewModel.ProjectDir;
        try
        {
            ViewModel.CancelExport();
        }
        catch { }

        MainWindow.CurrentInstance?.NavigateToEditor(pDir);
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
