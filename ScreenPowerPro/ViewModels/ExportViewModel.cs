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
    private bool _isExporting;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private string _selectedResolution = "1080p";

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

        _exportService.ProgressChanged += (pct) =>
        {
            ProgressPercent = Math.Round(pct, 1);
            StatusMessage = $"Render ediliyor: %{ProgressPercent:F0}";
        };

        _exportService.ExportCompleted += (path) =>
        {
            IsExporting = false;
            IsCompleted = true;
            ProgressPercent = 100;
            StatusMessage = "Video başarıyla dışa aktarıldı!";
        };

        _exportService.ExportFailed += (err) =>
        {
            IsExporting = false;
            StatusMessage = $"Hata: {err}";
        };
    }

    public void LoadProject(string projectDir)
    {
        ProjectDir = projectDir;
        IsCompleted = false;
        ProgressPercent = 0;
        StatusMessage = "Hazır";

        string fileName = $"{Path.GetFileName(projectDir)}.mp4";
        OutputPath = Path.Combine(projectDir, "bundle", fileName);
    }

    [RelayCommand]
    public async Task StartExportAsync()
    {
        if (IsExporting || string.IsNullOrEmpty(ProjectDir)) return;

        var manifest = _projectService.LoadProject(ProjectDir);
        if (manifest == null)
        {
            StatusMessage = "Proje bulunamadı!";
            return;
        }

        IsExporting = true;
        IsCompleted = false;
        ProgressPercent = 0;
        StatusMessage = "Render başlatılıyor...";

        _cts = new CancellationTokenSource();

        try
        {
            await _exportService.ExportVideoAsync(manifest, OutputPath, SelectedFps, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Dışa aktarma iptal edildi.";
            IsExporting = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata: {ex.Message}";
            IsExporting = false;
        }
    }

    [RelayCommand]
    public void CancelExport()
    {
        _cts?.Cancel();
        _exportService.CancelExport();
        IsExporting = false;
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
