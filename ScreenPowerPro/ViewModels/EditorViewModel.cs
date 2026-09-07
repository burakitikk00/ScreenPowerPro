using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenPowerPro.Models;
using ScreenPowerPro.Services;

namespace ScreenPowerPro.ViewModels;

/// <summary>
/// Editör geri alma/yineleme (undo/redo) geçmişi için anlık durum kaydı (snapshot).
/// </summary>
public class HistorySnapshot
{
    public List<ZoomEffect> ZoomEffects { get; set; } = new();
    public TrackState VideoTrack { get; set; } = new();
    public TrackState MicTrack { get; set; } = new();
    public TrackState SysTrack { get; set; } = new();

    public static HistorySnapshot Create(
        IEnumerable<ZoomEffect> zooms,
        TrackState videoTrack,
        TrackState micTrack,
        TrackState sysTrack)
    {
        return new HistorySnapshot
        {
            ZoomEffects = zooms.Select(z => new ZoomEffect
            {
                Id = z.Id,
                Name = z.Name,
                StartTime = z.StartTime,
                Duration = z.Duration,
                TargetX = z.TargetX,
                TargetY = z.TargetY,
                Scale = z.Scale,
                Easing = z.Easing
            }).ToList(),
            VideoTrack = CloneTrack(videoTrack),
            MicTrack = CloneTrack(micTrack),
            SysTrack = CloneTrack(sysTrack)
        };
    }

    private static TrackState CloneTrack(TrackState t)
    {
        return new TrackState
        {
            Offset = t.Offset,
            TrimStart = t.TrimStart,
            TrimEnd = t.TrimEnd,
            Muted = t.Muted,
            Clips = t.Clips.Select(c => new ClipSegment
            {
                Id = c.Id,
                SourceStart = c.SourceStart,
                SourceEnd = c.SourceEnd,
                TrackOffset = c.TrackOffset
            }).ToList()
        };
    }
}

/// <summary>
/// Video düzenleyici ViewModel sınıfı. Electron mimarisindeki editorStore.ts
/// state yönetiminin tüm yeteneklerini (Undo/Redo, Parça/Klip bölme, Zoom motoru,
/// çoklu parça zaman çizelgesi, 16+ ayar alanı) WinUI 3 için eksiksiz sağlar.
/// </summary>
public partial class EditorViewModel : ObservableObject
{
    private readonly ProjectService _projectService;
    private readonly ZoomEngineService _zoomEngineService;

    // Maksimum geri alma adımı sayısı
    private const int MaxHistory = 50;
    private readonly List<HistorySnapshot> _history = new();
    private int _historyIndex = -1;

    // --- Proje ve Medya Bilgileri ---
    [ObservableProperty]
    private string _projectDir = string.Empty;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _videoPath = string.Empty;

    [ObservableProperty]
    private string? _micAudioPath;

    [ObservableProperty]
    private string? _systemAudioPath;

    // --- Oynatma ve Zaman Durumu ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedCurrentTime))]
    private double _currentTimeSec;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedTotalTime))]
    private double _totalDurationSec = 10;

    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>
    /// Timeline tıklamalarında VideoPreview bileşenine seek komutu iletmek için sayıcı.
    /// </summary>
    [ObservableProperty]
    private int _seekVersion;

    // --- Seçimler ve Araçlar ---
    [ObservableProperty]
    private ObservableCollection<ZoomEffect> _zoomEffects = new();

    [ObservableProperty]
    private ZoomEffect? _selectedZoomEffect;

    [ObservableProperty]
    private string? _selectedClipId;

    [ObservableProperty]
    private string _activeSidebarTab = "motion"; // motion, cursor, camera, audio, canvas

    [ObservableProperty]
    private string _activeEditorTool = "cursor"; // cursor, split, delete

    [ObservableProperty]
    private double _timelineZoom = 50; // % 10 - 200

    [ObservableProperty]
    private double _timelineScroll = 0;

    // --- Parçalar (Tracks) ---
    [ObservableProperty]
    private TrackState _videoTrack = new();

    [ObservableProperty]
    private TrackState _micTrack = new();

    [ObservableProperty]
    private TrackState _sysTrack = new();

    // --- Zaman Çizelgesi ve Görsel Ayarlar ---
    [ObservableProperty]
    private TimelineSettings _settings = new();

    // Kolay bağlama için hızlı ayar erişimleri
    public string CursorSmoothing
    {
        get => Settings.CursorSmoothing;
        set { if (Settings.CursorSmoothing != value) { Settings.CursorSmoothing = value; OnPropertyChanged(); } }
    }

    public double CursorSize
    {
        get => Settings.CursorSize;
        set { if (Math.Abs(Settings.CursorSize - value) > 0.01) { Settings.CursorSize = value; OnPropertyChanged(); } }
    }

    public bool CursorVisible
    {
        get => Settings.CursorVisible;
        set { if (Settings.CursorVisible != value) { Settings.CursorVisible = value; OnPropertyChanged(); } }
    }

    public string CursorStyle
    {
        get => Settings.CursorStyle;
        set { if (Settings.CursorStyle != value) { Settings.CursorStyle = value; OnPropertyChanged(); } }
    }

    public string ClickEffect
    {
        get => Settings.ClickEffect;
        set { if (Settings.ClickEffect != value) { Settings.ClickEffect = value; OnPropertyChanged(); } }
    }

    public bool CursorClickSound
    {
        get => Settings.CursorClickSound;
        set { if (Settings.CursorClickSound != value) { Settings.CursorClickSound = value; OnPropertyChanged(); } }
    }

    public bool MotionBlur
    {
        get => Settings.MotionBlur;
        set { if (Settings.MotionBlur != value) { Settings.MotionBlur = value; OnPropertyChanged(); } }
    }

    public double MotionBlurAmount
    {
        get => Settings.MotionBlurAmount;
        set { if (Math.Abs(Settings.MotionBlurAmount - value) > 0.01) { Settings.MotionBlurAmount = value; OnPropertyChanged(); } }
    }

    public double DefaultZoomScale
    {
        get => Settings.DefaultZoomScale;
        set { if (Math.Abs(Settings.DefaultZoomScale - value) > 0.01) { Settings.DefaultZoomScale = value; OnPropertyChanged(); } }
    }

    public double VideoSpeed
    {
        get => Settings.VideoSpeed;
        set { if (Math.Abs(Settings.VideoSpeed - value) > 0.01) { Settings.VideoSpeed = value; OnPropertyChanged(); } }
    }

    public string CanvasBackground
    {
        get => Settings.CanvasBackground;
        set { if (Settings.CanvasBackground != value) { Settings.CanvasBackground = value; OnPropertyChanged(); } }
    }

    public double BackgroundOpacity
    {
        get => Settings.BackgroundOpacity;
        set { if (Math.Abs(Settings.BackgroundOpacity - value) > 0.01) { Settings.BackgroundOpacity = value; OnPropertyChanged(); } }
    }

    public string AspectRatio
    {
        get => Settings.AspectRatio;
        set { if (Settings.AspectRatio != value) { Settings.AspectRatio = value; OnPropertyChanged(); } }
    }

    public string BackgroundStyle
    {
        get => Settings.BackgroundStyle;
        set { if (Settings.BackgroundStyle != value) { Settings.BackgroundStyle = value; OnPropertyChanged(); } }
    }

    public bool Watermark
    {
        get => Settings.Watermark;
        set { if (Settings.Watermark != value) { Settings.Watermark = value; OnPropertyChanged(); } }
    }

    public string WatermarkText
    {
        get => Settings.WatermarkText;
        set { if (Settings.WatermarkText != value) { Settings.WatermarkText = value; OnPropertyChanged(); } }
    }

    public bool ShowKeystrokes
    {
        get => Settings.ShowKeystrokes;
        set { if (Settings.ShowKeystrokes != value) { Settings.ShowKeystrokes = value; OnPropertyChanged(); } }
    }

    public bool CameraVisible
    {
        get => Settings.CameraVisible;
        set { if (Settings.CameraVisible != value) { Settings.CameraVisible = value; OnPropertyChanged(); } }
    }

    public string CameraShape
    {
        get => Settings.CameraShape;
        set { if (Settings.CameraShape != value) { Settings.CameraShape = value; OnPropertyChanged(); } }
    }

    public double CameraSize
    {
        get => Settings.CameraSize;
        set { if (Math.Abs(Settings.CameraSize - value) > 0.01) { Settings.CameraSize = value; OnPropertyChanged(); } }
    }

    public double MicVolume
    {
        get => Settings.MicVolume;
        set { if (Math.Abs(Settings.MicVolume - value) > 0.01) { Settings.MicVolume = value; OnPropertyChanged(); } }
    }

    public double SysVolume
    {
        get => Settings.SysVolume;
        set { if (Math.Abs(Settings.SysVolume - value) > 0.01) { Settings.SysVolume = value; OnPropertyChanged(); } }
    }

    // --- Geri Alma / Yineleme Durum Bayrakları ---
    public bool CanUndo => _historyIndex > 0;
    public bool CanRedo => _historyIndex >= 0 && _historyIndex < _history.Count - 1;

    // --- Formatlanmış Metinler ---
    public string FormattedCurrentTime => ZoomEngineService.FormatTimecode(CurrentTimeSec);
    public string FormattedTotalTime => ZoomEngineService.FormatTimecode(TotalDurationSec);

    // Dışa aktarıma yönlendirme olayı
    public event Action<string>? NavigateToExport;

    public EditorViewModel(ProjectService projectService, ZoomEngineService zoomEngineService)
    {
        _projectService = projectService;
        _zoomEngineService = zoomEngineService;
    }

    /// <summary>
    /// Medya dosyasının (video veya ses) mutlak veya göreceli yolunu akıllıca tespit eder.
    /// Proje dizini taşınmışsa veya uzantı (.mp4/.webm) değişmişse otomatik çözümler.
    /// </summary>
    private static string ResolveMediaPath(string projectDir, string? rawPath, string defaultFileName)
    {
        if (string.IsNullOrEmpty(rawPath)) return string.Empty;

        // 1. Mutlak yol ve mevcutsa
        if (Path.IsPathRooted(rawPath) && File.Exists(rawPath))
        {
            return rawPath;
        }

        // 2. Proje dizinine göreceli çözümle
        string trimmed = rawPath.TrimStart('.', '/', '\\');
        string combined = Path.GetFullPath(Path.Combine(projectDir, trimmed));
        if (File.Exists(combined))
        {
            return combined;
        }

        // 3. Standart recording klasörü kontrolü
        string defaultPath = Path.Combine(projectDir, "recording", defaultFileName);
        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }

        // 4. Alternatif uzantı kontrolü (.mp4 <-> .webm)
        if (defaultFileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            string webmPath = Path.ChangeExtension(defaultPath, ".webm");
            if (File.Exists(webmPath)) return webmPath;
        }
        else if (defaultFileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
        {
            string mp4Path = Path.ChangeExtension(defaultPath, ".mp4");
            if (File.Exists(mp4Path)) return mp4Path;
        }

        return combined;
    }

    /// <summary>
    /// Belirtilen dizindeki projeyi ve tüm zaman çizelgesi verilerini yükler.
    /// </summary>
    public void LoadProject(string projectDir)
    {
        ProjectDir = projectDir;
        var manifest = _projectService.LoadProject(projectDir);
        if (manifest == null) return;

        ProjectName = manifest.ProjectName;
        
        // Video ve ses dosyası yollarını akıllı çözümle
        VideoPath = ResolveMediaPath(projectDir, manifest.VideoPath, "display-0.mp4");
        MicAudioPath = !string.IsNullOrEmpty(manifest.MicAudioPath)
            ? ResolveMediaPath(projectDir, manifest.MicAudioPath, "microphone-0.wav")
            : null;
        SystemAudioPath = !string.IsNullOrEmpty(manifest.SystemAudioPath)
            ? ResolveMediaPath(projectDir, manifest.SystemAudioPath, "system_audio-0.wav")
            : null;

        double duration = manifest.Metadata.DurationSeconds;
        if (duration <= 0 && File.Exists(VideoPath))
        {
            duration = Helpers.FFmpegHelper.GetVideoDuration(VideoPath);
        }
        TotalDurationSec = duration > 0 ? duration : 10;
        CurrentTimeSec = 0;
        IsPlaying = false;

        // Zoom efektlerini yükle
        ZoomEffects.Clear();
        foreach (var z in manifest.Timeline.ZoomEffects)
        {
            ZoomEffects.Add(z);
        }

        // Ayarları yükle
        Settings = manifest.Timeline.Settings ?? new TimelineSettings();

        // Parçaları yükle veya klipleri süreden başlat
        VideoTrack = manifest.Timeline.VideoTrack ?? new TrackState();
        MicTrack = manifest.Timeline.MicTrack ?? new TrackState();
        SysTrack = manifest.Timeline.SysTrack ?? new TrackState();

        InitClipsFromDuration(TotalDurationSec);

        // Geçmişi temizle ve ilk durumu snapshot olarak ekle
        _history.Clear();
        _historyIndex = -1;
        PushHistory();

        if (ZoomEffects.Count > 0)
        {
            SelectedZoomEffect = ZoomEffects[0];
        }

        NotifyAllProperties();
    }

    /// <summary>
    /// Süreye göre her bir parça (video, mic, sys) için ilk tam boy klibi oluşturur.
    /// </summary>
    public void InitClipsFromDuration(double duration)
    {
        if (duration <= 0) return;

        if (VideoTrack.Clips.Count == 0)
        {
            VideoTrack.Clips.Add(new ClipSegment
            {
                Id = $"clip-video-{Guid.NewGuid():N}",
                SourceStart = 0,
                SourceEnd = duration,
                TrackOffset = 0
            });
        }

        if (MicTrack.Clips.Count == 0 && !string.IsNullOrEmpty(MicAudioPath))
        {
            MicTrack.Clips.Add(new ClipSegment
            {
                Id = $"clip-mic-{Guid.NewGuid():N}",
                SourceStart = 0,
                SourceEnd = duration,
                TrackOffset = 0
            });
        }

        if (SysTrack.Clips.Count == 0 && !string.IsNullOrEmpty(SystemAudioPath))
        {
            SysTrack.Clips.Add(new ClipSegment
            {
                Id = $"clip-sys-{Guid.NewGuid():N}",
                SourceStart = 0,
                SourceEnd = duration,
                TrackOffset = 0
            });
        }
    }

    /// <summary>
    /// Bir değişiklik öncesinde mevcut durumu geri alma geçmişine kaydeder.
    /// </summary>
    public void PushHistory()
    {
        var snap = HistorySnapshot.Create(ZoomEffects, VideoTrack, MicTrack, SysTrack);

        // İlerideki adımları sil
        if (_historyIndex >= 0 && _historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - (_historyIndex + 1));
        }

        _history.Add(snap);
        if (_history.Count > MaxHistory)
        {
            _history.RemoveAt(0);
        }
        _historyIndex = _history.Count - 1;

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>
    /// Yapılan son işlemi geri alır.
    /// </summary>
    [RelayCommand]
    public void Undo()
    {
        if (!CanUndo) return;

        _historyIndex--;
        ApplySnapshot(_history[_historyIndex]);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>
    /// Geri alınan işlemi yineler.
    /// </summary>
    [RelayCommand]
    public void Redo()
    {
        if (!CanRedo) return;

        _historyIndex++;
        ApplySnapshot(_history[_historyIndex]);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void ApplySnapshot(HistorySnapshot snap)
    {
        ZoomEffects.Clear();
        foreach (var z in snap.ZoomEffects)
        {
            ZoomEffects.Add(new ZoomEffect
            {
                Id = z.Id,
                Name = z.Name,
                StartTime = z.StartTime,
                Duration = z.Duration,
                TargetX = z.TargetX,
                TargetY = z.TargetY,
                Scale = z.Scale,
                Easing = z.Easing
            });
        }

        VideoTrack = new TrackState
        {
            Offset = snap.VideoTrack.Offset,
            TrimStart = snap.VideoTrack.TrimStart,
            TrimEnd = snap.VideoTrack.TrimEnd,
            Muted = snap.VideoTrack.Muted,
            Clips = snap.VideoTrack.Clips.Select(c => new ClipSegment
            {
                Id = c.Id,
                SourceStart = c.SourceStart,
                SourceEnd = c.SourceEnd,
                TrackOffset = c.TrackOffset
            }).ToList()
        };

        MicTrack = new TrackState
        {
            Offset = snap.MicTrack.Offset,
            TrimStart = snap.MicTrack.TrimStart,
            TrimEnd = snap.MicTrack.TrimEnd,
            Muted = snap.MicTrack.Muted,
            Clips = snap.MicTrack.Clips.Select(c => new ClipSegment
            {
                Id = c.Id,
                SourceStart = c.SourceStart,
                SourceEnd = c.SourceEnd,
                TrackOffset = c.TrackOffset
            }).ToList()
        };

        SysTrack = new TrackState
        {
            Offset = snap.SysTrack.Offset,
            TrimStart = snap.SysTrack.TrimStart,
            TrimEnd = snap.SysTrack.TrimEnd,
            Muted = snap.SysTrack.Muted,
            Clips = snap.SysTrack.Clips.Select(c => new ClipSegment
            {
                Id = c.Id,
                SourceStart = c.SourceStart,
                SourceEnd = c.SourceEnd,
                TrackOffset = c.TrackOffset
            }).ToList()
        };

        SelectedZoomEffect = ZoomEffects.FirstOrDefault();
        SelectedClipId = null;
    }

    /// <summary>
    /// Mevcut oynatma konumunda (Playhead) tüm parçalardaki klipleri ve zoom efektlerini ikiye böler.
    /// </summary>
    [RelayCommand]
    public void CutAtPlayhead()
    {
        double time = CurrentTimeSec;
        PushHistory();

        // 1. Parçalardaki klipleri böl
        VideoTrack.Clips = SplitClipsList(VideoTrack.Clips, time);
        MicTrack.Clips = SplitClipsList(MicTrack.Clips, time);
        SysTrack.Clips = SplitClipsList(SysTrack.Clips, time);

        // 2. Eğer playhead bir zoom efektinin içindeyse onu da iki parçaya böl
        var newZooms = new List<ZoomEffect>();
        foreach (var z in ZoomEffects)
        {
            double end = z.StartTime + z.Duration;
            if (time > z.StartTime && time < end)
            {
                // İlk yarı
                newZooms.Add(new ZoomEffect
                {
                    Id = z.Id,
                    Name = z.Name,
                    StartTime = z.StartTime,
                    Duration = Math.Round(time - z.StartTime, 2),
                    TargetX = z.TargetX,
                    TargetY = z.TargetY,
                    Scale = z.Scale,
                    Easing = z.Easing
                });

                // İkinci yarı
                newZooms.Add(new ZoomEffect
                {
                    Id = $"{z.Id}-split",
                    Name = $"{z.Name} (Bölünmüş)",
                    StartTime = Math.Round(time, 2),
                    Duration = Math.Round(end - time, 2),
                    TargetX = z.TargetX,
                    TargetY = z.TargetY,
                    Scale = z.Scale,
                    Easing = z.Easing
                });
            }
            else
            {
                newZooms.Add(z);
            }
        }

        ZoomEffects.Clear();
        foreach (var z in newZooms)
        {
            ZoomEffects.Add(z);
        }

        SaveProject();
    }

    private static List<ClipSegment> SplitClipsList(List<ClipSegment> clips, double time)
    {
        var result = new List<ClipSegment>();
        foreach (var clip in clips)
        {
            double clipDuration = clip.SourceEnd - clip.SourceStart;
            double clipEnd = clip.TrackOffset + clipDuration;

            if (time > clip.TrackOffset && time < clipEnd)
            {
                double splitPoint = clip.SourceStart + (time - clip.TrackOffset);
                result.Add(new ClipSegment
                {
                    Id = clip.Id,
                    SourceStart = clip.SourceStart,
                    SourceEnd = splitPoint,
                    TrackOffset = clip.TrackOffset
                });
                result.Add(new ClipSegment
                {
                    Id = $"clip-{Guid.NewGuid():N}",
                    SourceStart = splitPoint,
                    SourceEnd = clip.SourceEnd,
                    TrackOffset = time
                });
            }
            else
            {
                result.Add(clip);
            }
        }
        return result;
    }

    /// <summary>
    /// Seçili klip veya zoom efektini siler ve klipleri kaydırır.
    /// </summary>
    [RelayCommand]
    public void DeleteSelected()
    {
        if (!string.IsNullOrEmpty(SelectedClipId))
        {
            PushHistory();
            string cid = SelectedClipId;
            VideoTrack.Clips = RemoveClipAndShift(VideoTrack.Clips, cid);
            MicTrack.Clips = RemoveClipAndShift(MicTrack.Clips, cid);
            SysTrack.Clips = RemoveClipAndShift(SysTrack.Clips, cid);
            SelectedClipId = null;
            SaveProject();
        }
        else if (SelectedZoomEffect != null)
        {
            PushHistory();
            ZoomEffects.Remove(SelectedZoomEffect);
            SelectedZoomEffect = ZoomEffects.FirstOrDefault();
            SaveProject();
        }
    }

    private static List<ClipSegment> RemoveClipAndShift(List<ClipSegment> clips, string clipId)
    {
        int idx = clips.FindIndex(c => c.Id == clipId);
        if (idx == -1) return clips;

        var removed = clips[idx];
        double removedDuration = removed.SourceEnd - removed.SourceStart;
        var newClips = new List<ClipSegment>();

        for (int i = 0; i < clips.Count; i++)
        {
            if (i == idx) continue;
            var c = clips[i];
            if (c.TrackOffset > removed.TrackOffset)
            {
                c.TrackOffset = Math.Max(0, c.TrackOffset - removedDuration);
            }
            newClips.Add(c);
        }

        return newClips;
    }

    /// <summary>
    /// Parçanın sessize alınmasını (Mute) açar veya kapatır.
    /// </summary>
    [RelayCommand]
    public void ToggleMute(string trackType)
    {
        if (string.Equals(trackType, "video", StringComparison.OrdinalIgnoreCase))
            VideoTrack.Muted = !VideoTrack.Muted;
        else if (string.Equals(trackType, "mic", StringComparison.OrdinalIgnoreCase))
            MicTrack.Muted = !MicTrack.Muted;
        else if (string.Equals(trackType, "sys", StringComparison.OrdinalIgnoreCase))
            SysTrack.Muted = !SysTrack.Muted;

        OnPropertyChanged(nameof(VideoTrack));
        OnPropertyChanged(nameof(MicTrack));
        OnPropertyChanged(nameof(SysTrack));
    }

    /// <summary>
    /// Kullanıcı zaman çizelgesine tıkladığında belirtilen zamana sarar (seek).
    /// </summary>
    public void SeekTo(double time)
    {
        CurrentTimeSec = Math.Clamp(time, 0, TotalDurationSec);
        SeekVersion++;
    }

    [RelayCommand]
    public void AddZoomAtCurrentTime()
    {
        PushHistory();

        var newZoom = new ZoomEffect
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = $"Zoom {ZoomEffects.Count + 1}",
            StartTime = Math.Round(CurrentTimeSec, 2),
            Duration = 2.0,
            TargetX = 1920 / 2.0,
            TargetY = 1080 / 2.0,
            Scale = DefaultZoomScale,
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
            PushHistory();
            ZoomEffects.Remove(SelectedZoomEffect);
            SelectedZoomEffect = ZoomEffects.FirstOrDefault();
            SaveProject();
        }
    }

    /// <summary>
    /// Mevcut zaman çizelgesini, zoom efektlerini ve tüm ayarları proje manifest dosyasına kaydeder.
    /// </summary>
    [RelayCommand]
    public void SaveProject()
    {
        if (string.IsNullOrEmpty(ProjectDir)) return;

        var manifest = _projectService.LoadProject(ProjectDir) ?? new ProjectManifest();
        manifest.ProjectName = ProjectName;

        // Göreceli yol kullan
        if (!string.IsNullOrEmpty(VideoPath))
        {
            manifest.VideoPath = Path.IsPathRooted(VideoPath)
                ? Path.GetRelativePath(ProjectDir, VideoPath).Replace('\\', '/')
                : VideoPath;
        }

        manifest.Timeline.ZoomEffects = new(ZoomEffects);
        manifest.Timeline.Settings = Settings;
        manifest.Timeline.VideoTrack = VideoTrack;
        manifest.Timeline.MicTrack = MicTrack;
        manifest.Timeline.SysTrack = SysTrack;

        _projectService.SaveProject(ProjectDir, manifest);
    }

    [RelayCommand]
    public void Export()
    {
        SaveProject();
        NavigateToExport?.Invoke(ProjectDir);
    }

    /// <summary>
    /// Belirli bir zamandaki canlı zoom transformunu hesaplamak için motordan faydalanır.
    /// </summary>
    public ZoomEngineService.ActiveZoomState? GetCurrentZoom()
    {
        return _zoomEngineService.GetActiveZoomAtTime(ZoomEffects.ToList(), CurrentTimeSec);
    }

    private void NotifyAllProperties()
    {
        OnPropertyChanged(nameof(CursorSmoothing));
        OnPropertyChanged(nameof(CursorSize));
        OnPropertyChanged(nameof(CursorVisible));
        OnPropertyChanged(nameof(CursorStyle));
        OnPropertyChanged(nameof(ClickEffect));
        OnPropertyChanged(nameof(CursorClickSound));
        OnPropertyChanged(nameof(MotionBlur));
        OnPropertyChanged(nameof(MotionBlurAmount));
        OnPropertyChanged(nameof(DefaultZoomScale));
        OnPropertyChanged(nameof(VideoSpeed));
        OnPropertyChanged(nameof(CanvasBackground));
        OnPropertyChanged(nameof(BackgroundOpacity));
        OnPropertyChanged(nameof(AspectRatio));
        OnPropertyChanged(nameof(BackgroundStyle));
        OnPropertyChanged(nameof(Watermark));
        OnPropertyChanged(nameof(WatermarkText));
        OnPropertyChanged(nameof(ShowKeystrokes));
        OnPropertyChanged(nameof(CameraVisible));
        OnPropertyChanged(nameof(CameraShape));
        OnPropertyChanged(nameof(CameraSize));
        OnPropertyChanged(nameof(MicVolume));
        OnPropertyChanged(nameof(SysVolume));
    }
}
