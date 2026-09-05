using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenPowerPro.Models;
using ScreenPowerPro.Services;

namespace ScreenPowerPro.ViewModels;

public partial class EditorViewModel : ObservableObject
{
    private readonly ProjectService _projectService;

    [ObservableProperty]
    private string _projectDir = string.Empty;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _videoPath = string.Empty;

    [ObservableProperty]
    private double _currentTimeSec;

    [ObservableProperty]
    private double _totalDurationSec = 10;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private ObservableCollection<ZoomEffect> _zoomEffects = new();

    [ObservableProperty]
    private ZoomEffect? _selectedZoomEffect;

    [ObservableProperty]
    private string _cursorSmoothing = "medium";

    [ObservableProperty]
    private bool _motionBlur = true;

    [ObservableProperty]
    private bool _showKeystrokes = true;

    [ObservableProperty]
    private string _backgroundStyle = "gradient-1";

    public string FormattedCurrentTime => TimeSpan.FromSeconds(CurrentTimeSec).ToString(@"mm\:ss\.ff");
    public string FormattedTotalTime => TimeSpan.FromSeconds(TotalDurationSec).ToString(@"mm\:ss\.ff");

    public event Action<string>? NavigateToExport; // passes projectDir

    public EditorViewModel(ProjectService projectService)
    {
        _projectService = projectService;
    }

    public void LoadProject(string projectDir)
    {
        ProjectDir = projectDir;
        var manifest = _projectService.LoadProject(projectDir);
        if (manifest == null) return;

        ProjectName = manifest.ProjectName;
        VideoPath = manifest.VideoPath;
        TotalDurationSec = manifest.Metadata.DurationSeconds > 0 ? manifest.Metadata.DurationSeconds : 10;
        CurrentTimeSec = 0;

        ZoomEffects.Clear();
        foreach (var z in manifest.Timeline.ZoomEffects)
        {
            ZoomEffects.Add(z);
        }

        CursorSmoothing = manifest.Timeline.Settings.CursorSmoothing;
        MotionBlur = manifest.Timeline.Settings.MotionBlur;
        ShowKeystrokes = manifest.Timeline.Settings.ShowKeystrokes;
        BackgroundStyle = manifest.Timeline.Settings.BackgroundStyle;

        if (ZoomEffects.Count > 0)
        {
            SelectedZoomEffect = ZoomEffects[0];
        }
    }

    [RelayCommand]
    public void AddZoomAtCurrentTime()
    {
        var newZoom = new ZoomEffect
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = $"Zoom {ZoomEffects.Count + 1}",
            StartTime = Math.Round(CurrentTimeSec, 2),
            Duration = 2.0,
            TargetX = 1920 / 2,
            TargetY = 1080 / 2,
            Scale = 1.5,
            Easing = "ease-in-out"
        };

        ZoomEffects.Add(newZoom);
        SelectedZoomEffect = newZoom;
        SaveProject();
    }

    [RelayCommand]
    public void DeleteSelectedZoom()
    {
        if (SelectedZoomEffect != null)
        {
            ZoomEffects.Remove(SelectedZoomEffect);
            SelectedZoomEffect = ZoomEffects.Count > 0 ? ZoomEffects[0] : null;
            SaveProject();
        }
    }

    [RelayCommand]
    public void SaveProject()
    {
        if (string.IsNullOrEmpty(ProjectDir)) return;

        var manifest = _projectService.LoadProject(ProjectDir) ?? new ProjectManifest();
        manifest.ProjectName = ProjectName;
        manifest.VideoPath = VideoPath;
        manifest.Timeline.ZoomEffects = new(ZoomEffects);
        manifest.Timeline.Settings.CursorSmoothing = CursorSmoothing;
        manifest.Timeline.Settings.MotionBlur = MotionBlur;
        manifest.Timeline.Settings.ShowKeystrokes = ShowKeystrokes;
        manifest.Timeline.Settings.BackgroundStyle = BackgroundStyle;

        _projectService.SaveProject(ProjectDir, manifest);
    }

    [RelayCommand]
    public void Export()
    {
        SaveProject();
        NavigateToExport?.Invoke(ProjectDir);
    }
}
