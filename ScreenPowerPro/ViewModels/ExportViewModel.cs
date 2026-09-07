using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenPowerPro.Models;
using ScreenPowerPro.Services;

namespace ScreenPowerPro.ViewModels;

public partial class ExportViewModel : ObservableObject
{
    private readonly ExportService _exportService;
    private readonly ProjectService _projectService;
    private readonly SettingsService _settingsService;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _projectDir = string.Empty;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _statusMessage = "Hazır";

    [ObservableProperty]
    private string _estimatedTimeRemaining = "Hesaplanıyor...";

    [ObservableProperty]
    private double _renderSpeed = 1.0;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _selectedResolution = "1080p";

    [ObservableProperty]
    private int _targetWidth = 1920;

    [ObservableProperty]
    private int _targetHeight = 1080;

    [ObservableProperty]
    private int _selectedFps = 60;

    public ExportViewModel(
        ExportService exportService,
        ProjectService projectService,
        SettingsService settingsService)
    {
        _exportService = exportService;
        _projectService = projectService;
        _settingsService = settingsService;

        _exportService.ProgressUpdated += (report) =>
        {
            ProgressPercent = Math.Round(report.ProgressPercent, 1);
            RenderSpeed = report.Speed;
            EstimatedTimeRemaining = report.FormattedRemainingTime;
            StatusMessage = report.Speed > 0
                ? $"Render ediliyor: %{ProgressPercent:F0} ({report.Speed:F1}x)"
                : $"Render ediliyor: %{ProgressPercent:F0}";
        };

        _exportService.ProgressChanged += (pct) =>
        {
            ProgressPercent = Math.Round(pct, 1);
        };

        _exportService.ExportCompleted += (path) =>
        {
            IsExporting = false;
            IsCompleted = true;
            HasError = false;
            ProgressPercent = 100;
            EstimatedTimeRemaining = "00:00";
            StatusMessage = "Video başarıyla dışa aktarıldı!";
        };

        _exportService.ExportFailed += (err) =>
        {
            if (_cts?.IsCancellationRequested == true) return;
            IsExporting = false;
            HasError = true;
            ErrorMessage = err;
            EstimatedTimeRemaining = "Hata oluştu";
            StatusMessage = $"Hata: {err}";
        };
    }

    public void LoadProject(string projectDir)
    {
        ProjectDir = projectDir;
        IsCompleted = false;
        HasError = false;
        ErrorMessage = string.Empty;
        ProgressPercent = 0;
        EstimatedTimeRemaining = "Hesaplanıyor...";
        StatusMessage = "Hazır";
        TargetWidth = 1920;
        TargetHeight = 1080;

        string fileName = $"{Path.GetFileName(projectDir)}.mp4";
        OutputPath = Path.Combine(projectDir, "bundle", fileName);
    }

    public void LoadProjectWithOptions(ExportOptions options)
    {
        ProjectDir = options.ProjectDir;
        OutputPath = !string.IsNullOrWhiteSpace(options.OutputPath)
            ? options.OutputPath
            : Path.Combine(options.ProjectDir, "bundle", $"{Path.GetFileName(options.ProjectDir)}.mp4");
        TargetWidth = options.TargetWidth > 0 ? options.TargetWidth : 1920;
        TargetHeight = options.TargetHeight > 0 ? options.TargetHeight : 1080;
        SelectedFps = options.TargetFps > 0 ? options.TargetFps : 60;
        SelectedResolution = $"{TargetWidth}×{TargetHeight}";
        IsCompleted = false;
        HasError = false;
        ErrorMessage = string.Empty;
        ProgressPercent = 0;
        EstimatedTimeRemaining = "Hesaplanıyor...";
        StatusMessage = "Hazır";
    }

    [RelayCommand]
    public async Task StartExportAsync()
    {
        if (IsExporting || string.IsNullOrEmpty(ProjectDir)) return;

        var manifest = _projectService.LoadProject(ProjectDir);
        if (manifest == null)
        {
            StatusMessage = "Proje bulunamadı!";
            HasError = true;
            ErrorMessage = "Proje bulunamadı!";
            return;
        }

        IsExporting = true;
        IsCompleted = false;
        HasError = false;
        ErrorMessage = string.Empty;
        ProgressPercent = 0;
        EstimatedTimeRemaining = "Hesaplanıyor...";
        StatusMessage = "Render başlatılıyor...";

        _cts = new CancellationTokenSource();

        try
        {
            await _exportService.ExportVideoAsync(
                manifest,
                OutputPath,
                ProjectDir,
                TargetWidth,
                TargetHeight,
                SelectedFps,
                _cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Dışa aktarma iptal edildi.";
            IsExporting = false;
            HasError = false;
        }
        catch (Exception ex)
        {
            if (_cts?.IsCancellationRequested == true)
            {
                StatusMessage = "Dışa aktarma iptal edildi.";
                IsExporting = false;
                HasError = false;
                return;
            }
            StatusMessage = $"Hata: {ex.Message}";
            HasError = true;
            ErrorMessage = ex.Message;
            IsExporting = false;
        }
    }

    [RelayCommand]
    public void CancelExport()
    {
        try
        {
            _cts?.Cancel();
            _exportService.CancelExport();
        }
        catch { }
        IsExporting = false;
        HasError = false;
        StatusMessage = "İptal edildi.";
    }

    [RelayCommand]
    public void OpenOutputFolder()
    {
        if (File.Exists(OutputPath))
        {
            Process.Start("explorer.exe", $"/select,\"{OutputPath}\"");
        }
        else if (Directory.Exists(Path.GetDirectoryName(OutputPath)))
        {
            Process.Start("explorer.exe", Path.GetDirectoryName(OutputPath)!);
        }
    }

    [RelayCommand]
    public void PlayVideo()
    {
        if (File.Exists(OutputPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = OutputPath,
                UseShellExecute = true
            });
        }
    }
}
