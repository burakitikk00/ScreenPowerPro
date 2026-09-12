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
    private readonly AudioWaveformService _waveformService;

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

    partial void OnSelectedZoomEffectChanged(ZoomEffect? value)
    {
        OnPropertyChanged(nameof(SelectedZoomScale));
        OnPropertyChanged(nameof(SelectedZoomEasing));
        OnPropertyChanged(nameof(SelectedZoomDuration));
    }

    public double SelectedZoomScale
    {
        get => SelectedZoomEffect?.Scale ?? 1.5;
        set
        {
            if (SelectedZoomEffect != null)
            {
                var clamped = Math.Clamp(value, 1.0, 2.2);
                if (SelectedZoomEffect.Scale != clamped)
                {
                    SelectedZoomEffect.Scale = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedZoomEffect));
                }
            }
        }
    }

    public string SelectedZoomEasing
    {
        get => SelectedZoomEffect?.Easing ?? "Cubic-Out";
        set
        {
            if (SelectedZoomEffect != null && SelectedZoomEffect.Easing != value)
            {
                SelectedZoomEffect.Easing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedZoomEffect));
            }
        }
    }

    public double SelectedZoomDuration
    {
        get => SelectedZoomEffect?.Duration ?? 2.0;
        set
        {
            if (SelectedZoomEffect != null && value >= 0.2)
            {
                var rounded = Math.Round(value, 2);
                if (SelectedZoomEffect.Duration != rounded)
                {
                    SelectedZoomEffect.Duration = rounded;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedZoomEffect));
                }
            }
        }
    }

    [ObservableProperty]
    private string? _selectedClipId;

    public ObservableCollection<string> SelectedClipIds { get; } = new();

    public bool IsClipSelected(string? id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        return SelectedClipIds.Contains(id) || SelectedClipId == id;
    }

    public void SelectClip(string? id, bool isMultiSelect = false)
    {
        if (string.IsNullOrEmpty(id))
        {
            ClearClipSelection();
            return;
        }

        if (!isMultiSelect)
        {
            SelectedClipIds.Clear();
            SelectedClipIds.Add(id);
            SelectedClipId = id;
        }
        else
        {
            if (SelectedClipIds.Contains(id))
            {
                SelectedClipIds.Remove(id);
                SelectedClipId = SelectedClipIds.LastOrDefault();
            }
            else
            {
                if (!string.IsNullOrEmpty(SelectedClipId) && !SelectedClipIds.Contains(SelectedClipId))
                {
                    SelectedClipIds.Add(SelectedClipId);
                }
                SelectedClipIds.Add(id);
                SelectedClipId = id;
            }
        }
        OnPropertyChanged(nameof(SelectedClipId));
    }

    public void ClearClipSelection()
    {
        SelectedClipIds.Clear();
        SelectedClipId = null;
        OnPropertyChanged(nameof(SelectedClipId));
    }

    public void SelectAllClipsInTrack(string trackType)
    {
        SelectedClipIds.Clear();
        SelectedClipId = null;
        List<ClipSegment>? clips = trackType switch
        {
            "video" => VideoTrack.Clips,
            "mic" or "audio" => MicTrack.Clips,
            "sys" => SysTrack.Clips,
            _ => null
        };

        if (clips != null && clips.Count > 0)
        {
            foreach (var c in clips)
            {
                SelectedClipIds.Add(c.Id);
            }
            SelectedClipId = clips.LastOrDefault()?.Id;
            SelectedTrackType = trackType == "audio" ? "mic" : trackType;
        }
        OnPropertyChanged(nameof(SelectedClipId));
    }

    [ObservableProperty]
    private string? _selectedTrackType; // "video", "mic", "sys"

    [ObservableProperty]
    private float[]? _waveformPeaks;

    [ObservableProperty]
    private string _activeSidebarTab = "cursor"; // cursor, canvas, audio, keys, motion, camera, watermark

    [ObservableProperty]
    private string _activeEditorTool = "cursor"; // cursor, split, delete

    [ObservableProperty]
    private double _timelineZoom = 50; // % 10 - 200

    public List<MouseMoveEvent> MouseMoves { get; private set; } = new();
    public List<MouseClickEvent> MouseClicks { get; private set; } = new();
    public List<KeystrokeEvent> Keystrokes { get; private set; } = new();

    [ObservableProperty]
    private int _videoWidth = 1920;

    [ObservableProperty]
    private int _videoHeight = 1080;

    [ObservableProperty]
    private double _timelineScroll = 0;

    // --- Parçalar (Tracks) ---
    [ObservableProperty]
    private TrackState _videoTrack = new();

    [ObservableProperty]
    private TrackState _micTrack = new();

    [ObservableProperty]
    private TrackState _sysTrack = new();

    [ObservableProperty]
    private TrackState _clickTrack = new();

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

    public string CursorClickSoundFile
    {
        get => string.IsNullOrEmpty(Settings.CursorClickSoundFile) ? "universfield-computer-mouse-click-02-383961.mp3" : Settings.CursorClickSoundFile;
        set { if (Settings.CursorClickSoundFile != value) { Settings.CursorClickSoundFile = value; OnPropertyChanged(); } }
    }

    public double CursorClickVolume
    {
        get => Settings.CursorClickVolume;
        set { if (Math.Abs(Settings.CursorClickVolume - value) > 0.01) { Settings.CursorClickVolume = value; OnPropertyChanged(); } }
    }

    public IReadOnlyList<ClickSoundItem> AvailableClickSounds => ClickSoundService.Instance.AvailableSounds;

    public bool HideCursorWhenIdle
    {
        get => Settings.HideCursorWhenIdle;
        set { if (Settings.HideCursorWhenIdle != value) { Settings.HideCursorWhenIdle = value; OnPropertyChanged(); } }
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
        set { double clamped = Math.Clamp(value, 1.0, 2.2); if (Math.Abs(Settings.DefaultZoomScale - clamped) > 0.01) { Settings.DefaultZoomScale = clamped; OnPropertyChanged(); } }
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

    public bool MicMuted
    {
        get => Settings.MicMuted;
        set { if (Settings.MicMuted != value) { Settings.MicMuted = value; OnPropertyChanged(); } }
    }

    public double VolumeEnhancement
    {
        get => Settings.VolumeEnhancement;
        set { if (Math.Abs(Settings.VolumeEnhancement - value) > 0.01) { Settings.VolumeEnhancement = value; OnPropertyChanged(); } }
    }

    public bool AudioNoiseReduction
    {
        get => Settings.AudioNoiseReduction;
        set { if (Settings.AudioNoiseReduction != value) { Settings.AudioNoiseReduction = value; OnPropertyChanged(); } }
    }

    public bool VocalEnhancement
    {
        get => Settings.VocalEnhancement;
        set { if (Settings.VocalEnhancement != value) { Settings.VocalEnhancement = value; OnPropertyChanged(); } }
    }

    public int VocalEnhancementAmount
    {
        get => Settings.VocalEnhancementAmount;
        set { if (Settings.VocalEnhancementAmount != value) { Settings.VocalEnhancementAmount = value; OnPropertyChanged(); } }
    }

    // Canvas ayarları
    public int Padding
    {
        get => Settings.Padding;
        set { if (Settings.Padding != value) { Settings.Padding = value; OnPropertyChanged(); } }
    }

    public int Inset
    {
        get => Settings.Inset;
        set { if (Settings.Inset != value) { Settings.Inset = value; OnPropertyChanged(); } }
    }

    public int Roundness
    {
        get => Settings.Roundness;
        set { if (Settings.Roundness != value) { Settings.Roundness = value; OnPropertyChanged(); } }
    }

    public int Shadow
    {
        get => Settings.Shadow;
        set { if (Settings.Shadow != value) { Settings.Shadow = value; OnPropertyChanged(); } }
    }

    public bool FixedZoomPart
    {
        get => Settings.FixedZoomPart;
        set { if (Settings.FixedZoomPart != value) { Settings.FixedZoomPart = value; OnPropertyChanged(); } }
    }

    public string CanvasPreset
    {
        get => Settings.CanvasPreset;
        set { if (Settings.CanvasPreset != value) { Settings.CanvasPreset = value; OnPropertyChanged(); } }
    }

    // Motion ayarları
    public bool ZoomInMotionBlur
    {
        get => Settings.ZoomInMotionBlur;
        set { if (Settings.ZoomInMotionBlur != value) { Settings.ZoomInMotionBlur = value; OnPropertyChanged(); } }
    }

    public double ZoomInMotionBlurAmount
    {
        get => Settings.ZoomInMotionBlurAmount;
        set { if (Math.Abs(Settings.ZoomInMotionBlurAmount - value) > 0.01) { Settings.ZoomInMotionBlurAmount = value; OnPropertyChanged(); } }
    }

    public double ScreenMotionBlurAmount
    {
        get => Settings.ScreenMotionBlurAmount;
        set { if (Math.Abs(Settings.ScreenMotionBlurAmount - value) > 0.01) { Settings.ScreenMotionBlurAmount = value; OnPropertyChanged(); } }
    }

    public double CursorMotionBlurAmount
    {
        get => Settings.CursorMotionBlurAmount;
        set { if (Math.Abs(Settings.CursorMotionBlurAmount - value) > 0.01) { Settings.CursorMotionBlurAmount = value; OnPropertyChanged(); } }
    }

    public string ZoomPanMovementType
    {
        get => Settings.ZoomPanMovementType;
        set { if (Settings.ZoomPanMovementType != value) { Settings.ZoomPanMovementType = value; OnPropertyChanged(); } }
    }

    public string CursorMovementType
    {
        get => Settings.CursorMovementType;
        set { if (Settings.CursorMovementType != value) { Settings.CursorMovementType = value; OnPropertyChanged(); } }
    }

    // Kısayol Tuşları ayarları
    public string ShortcutKeyStyle
    {
        get => Settings.ShortcutKeyStyle;
        set { if (Settings.ShortcutKeyStyle != value) { Settings.ShortcutKeyStyle = value; OnPropertyChanged(); } }
    }

    public string ShortcutFontColor
    {
        get => Settings.ShortcutFontColor;
        set { if (Settings.ShortcutFontColor != value) { Settings.ShortcutFontColor = value; OnPropertyChanged(); } }
    }

    public string ShortcutBgColor
    {
        get => Settings.ShortcutBgColor;
        set { if (Settings.ShortcutBgColor != value) { Settings.ShortcutBgColor = value; OnPropertyChanged(); } }
    }

    public int ShortcutBgOpacity
    {
        get => Settings.ShortcutBgOpacity;
        set { if (Settings.ShortcutBgOpacity != value) { Settings.ShortcutBgOpacity = value; OnPropertyChanged(); } }
    }

    public int ShortcutSize
    {
        get => Settings.ShortcutSize;
        set { if (Settings.ShortcutSize != value) { Settings.ShortcutSize = value; OnPropertyChanged(); } }
    }

    public string ShortcutPosition
    {
        get => Settings.ShortcutPosition;
        set { if (Settings.ShortcutPosition != value) { Settings.ShortcutPosition = value; OnPropertyChanged(); } }
    }

    public bool DisplaySingleShortcutKey
    {
        get => Settings.DisplaySingleShortcutKey;
        set { if (Settings.DisplaySingleShortcutKey != value) { Settings.DisplaySingleShortcutKey = value; OnPropertyChanged(); } }
    }

    // --- Dinamik Zoom Ayarları (Global SettingsManager üzerinden) ---
    public string ZoomEasingFunction
    {
        get => SettingsManager.Instance.ZoomEasingFunction;
        set { if (SettingsManager.Instance.ZoomEasingFunction != value) { SettingsManager.Instance.ZoomEasingFunction = value; OnPropertyChanged(); } }
    }

    public double ZoomSpeedMs
    {
        get => SettingsManager.Instance.ZoomSpeed * 1000.0;
        set { if (Math.Abs(SettingsManager.Instance.ZoomSpeed * 1000.0 - value) > 0.01) { SettingsManager.Instance.ZoomSpeed = value / 1000.0; OnPropertyChanged(); } }
    }

    public double ZoomHoldDurationSec
    {
        get => SettingsManager.Instance.ZoomDuration;
        set { if (Math.Abs(SettingsManager.Instance.ZoomDuration - value) > 0.01) { SettingsManager.Instance.ZoomDuration = value; OnPropertyChanged(); } }
    }

    public double ZoomMaxScale
    {
        get => SettingsManager.Instance.MaxZoomRatio;
        set { double clamped = Math.Clamp(value, 1.0, 2.2); if (Math.Abs(SettingsManager.Instance.MaxZoomRatio - clamped) > 0.01) { SettingsManager.Instance.MaxZoomRatio = clamped; OnPropertyChanged(); } }
    }

    public int PreClickAnticipationMs
    {
        get => SettingsManager.Instance.PreClickAnticipationMs;
        set { if (SettingsManager.Instance.PreClickAnticipationMs != value) { SettingsManager.Instance.PreClickAnticipationMs = value; OnPropertyChanged(); } }
    }

    // --- Geri Alma / Yineleme Durum Bayrakları ---
    public bool CanUndo => _historyIndex > 0;
    public bool CanRedo => _historyIndex >= 0 && _historyIndex < _history.Count - 1;

    // --- Formatlanmış Metinler ---
    public string FormattedCurrentTime => ZoomEngineService.FormatTimecode(CurrentTimeSec);
    public string FormattedTotalTime => ZoomEngineService.FormatTimecode(TotalDurationSec);

    // Dışa aktarıma yönlendirme olayı
    public event Action<string>? NavigateToExport;

    public EditorViewModel(ProjectService projectService, ZoomEngineService zoomEngineService, AudioWaveformService waveformService)
    {
        _projectService = projectService;
        _zoomEngineService = zoomEngineService;
        _waveformService = waveformService;
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
        if (!string.IsNullOrEmpty(MicAudioPath) && (!File.Exists(MicAudioPath) || new FileInfo(MicAudioPath).Length <= 200))
        {
            MicAudioPath = null;
        }

        SystemAudioPath = !string.IsNullOrEmpty(manifest.SystemAudioPath)
            ? ResolveMediaPath(projectDir, manifest.SystemAudioPath, "system_audio-0.wav")
            : null;
        if (!string.IsNullOrEmpty(SystemAudioPath) && (!File.Exists(SystemAudioPath) || new FileInfo(SystemAudioPath).Length <= 200))
        {
            SystemAudioPath = null;
        }

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
        ClickTrack = manifest.Timeline.ClickTrack ?? new TrackState();

        InitClipsFromDuration(TotalDurationSec);

        if (string.IsNullOrEmpty(MicAudioPath))
        {
            MicTrack.Clips.Clear();
        }
        if (string.IsNullOrEmpty(SystemAudioPath))
        {
            SysTrack.Clips.Clear();
        }

        // Geçmişi temizle ve ilk durumu snapshot olarak ekle
        _history.Clear();
        _historyIndex = -1;
        PushHistory();

        if (ZoomEffects.Count > 0)
        {
            SelectedZoomEffect = ZoomEffects[0];
        }

        if (manifest.Metadata != null)
        {
            if (manifest.Metadata.Width > 0) VideoWidth = manifest.Metadata.Width;
            if (manifest.Metadata.Height > 0) VideoHeight = manifest.Metadata.Height;
        }

        // Telemetri verilerini yükle
        MouseClicks = _projectService.LoadMouseClicks(projectDir);
        MouseMoves = _projectService.LoadMouseMoves(projectDir);
        Keystrokes = _projectService.LoadKeystrokes(projectDir);

        // Telemetri verilerine göre Zoom hedeflerini kesinleştir
        if (ZoomEffects.Count > 0)
        {
            foreach (var z in ZoomEffects)
            {
                MouseClickEvent? matchedClick = null;
                if (MouseClicks != null && MouseClicks.Count > 0)
                {
                    matchedClick = MouseClicks
                        .Where(c => c.Timestamp >= z.StartTime - 0.3 && c.Timestamp <= z.StartTime + z.Duration)
                        .OrderBy(c => Math.Abs(c.Timestamp - (z.StartTime + 0.15)))
                        .FirstOrDefault();
                }

                if (matchedClick != null)
                {
                    z.TargetX = Math.Round((double)matchedClick.X, 1);
                    z.TargetY = Math.Round((double)matchedClick.Y, 1);
                }
                else if (MouseMoves != null && MouseMoves.Count > 0)
                {
                    var pt = ZoomEngineService.GetInterpolatedCursorPosition(MouseMoves, z.StartTime + 0.15) 
                             ?? ZoomEngineService.GetInterpolatedCursorPosition(MouseMoves, z.StartTime);
                    if (pt.HasValue && (z.TargetX <= 0 || (Math.Abs(z.TargetX - 960) < 1 && Math.Abs(z.TargetY - 540) < 1)))
                    {
                        z.TargetX = Math.Round(pt.Value.X, 1);
                        z.TargetY = Math.Round(pt.Value.Y, 1);
                    }
                }
            }
        }

        LoadWaveformData();
        NotifyAllProperties();
    }

    /// <summary>
    /// Ses dosyasından dalga formu peak verilerini çeker.
    /// </summary>
    public void LoadWaveformData()
    {
        try
        {
            string? audioFile = !string.IsNullOrEmpty(MicAudioPath) && File.Exists(MicAudioPath)
                ? MicAudioPath
                : (!string.IsNullOrEmpty(SystemAudioPath) && File.Exists(SystemAudioPath)
                    ? SystemAudioPath
                    : (!string.IsNullOrEmpty(VideoPath) && File.Exists(VideoPath) ? VideoPath : null));

            WaveformPeaks = _waveformService.ExtractPeaks(audioFile, 1200);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EditorViewModel] Waveform load error: {ex.Message}");
            WaveformPeaks = null;
        }
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
                double part1Dur = Math.Round(time - z.StartTime, 2);
                if (part1Dur < 0.1 || (z.Duration - part1Dur) < 0.1)
                {
                    newZooms.Add(z);
                    continue;
                }

                // İlk yarı: startTime: eski_start, duration: T - eski_start
                newZooms.Add(new ZoomEffect
                {
                    Id = z.Id,
                    Name = z.Name,
                    StartTime = z.StartTime,
                    Duration = part1Dur,
                    TargetX = z.TargetX,
                    TargetY = z.TargetY,
                    Scale = Math.Clamp(z.Scale, 1.0, 2.2),
                    Easing = z.Easing
                });

                // İkinci yarı: startTime: T, duration: eski_duration - (T - eski_start)
                newZooms.Add(new ZoomEffect
                {
                    Id = $"{z.Id}-split",
                    Name = $"{z.Name} (Bölünmüş)",
                    StartTime = Math.Round(z.StartTime + part1Dur, 2),
                    Duration = Math.Round(z.Duration - part1Dur, 2),
                    TargetX = z.TargetX,
                    TargetY = z.TargetY,
                    Scale = Math.Clamp(z.Scale, 1.0, 2.2),
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
                double part1Duration = Math.Round(time - clip.TrackOffset, 2);
                if (part1Duration < 0.05 || (clipDuration - part1Duration) < 0.05)
                {
                    result.Add(clip);
                    continue;
                }

                double splitPoint = Math.Round(clip.SourceStart + part1Duration, 2);
                double part2Offset = Math.Round(clip.TrackOffset + part1Duration, 2);

                // Parça 1: startTime: eski_start, duration: T - eski_start
                result.Add(new ClipSegment
                {
                    Id = clip.Id,
                    SourceStart = clip.SourceStart,
                    SourceEnd = splitPoint,
                    TrackOffset = clip.TrackOffset
                });
                // Parça 2: startTime: T, duration: eski_duration - (T - eski_start)
                result.Add(new ClipSegment
                {
                    Id = $"clip-{Guid.NewGuid():N}",
                    SourceStart = splitPoint,
                    SourceEnd = clip.SourceEnd,
                    TrackOffset = part2Offset
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
        var idsToDelete = new List<string>();
        if (SelectedClipIds.Count > 0)
        {
            idsToDelete.AddRange(SelectedClipIds);
        }
        else if (!string.IsNullOrEmpty(SelectedClipId))
        {
            idsToDelete.Add(SelectedClipId);
        }

        if (idsToDelete.Count > 0)
        {
            PushHistory();
            foreach (var cid in idsToDelete)
            {
                var vClip = VideoTrack.Clips.FirstOrDefault(c => c.Id == cid);
                if (vClip != null && vClip.IsLocked) continue;
                var mClip = MicTrack.Clips.FirstOrDefault(c => c.Id == cid);
                if (mClip != null && mClip.IsLocked) continue;

                bool inVideo = vClip != null;
                bool inMic = mClip != null;
                bool inSys = SysTrack.Clips.Any(c => c.Id == cid);

                if (SelectedTrackType == "video" || (inVideo && !inMic && !inSys))
                {
                    VideoTrack.Clips = RemoveClip(VideoTrack.Clips, cid);
                }
                else if (SelectedTrackType == "mic" || (inMic && !inVideo && !inSys))
                {
                    MicTrack.Clips = RemoveClip(MicTrack.Clips, cid);
                }
                else if (SelectedTrackType == "sys" || (inSys && !inVideo && !inMic))
                {
                    SysTrack.Clips = RemoveClip(SysTrack.Clips, cid);
                }
                else
                {
                    if (inVideo) VideoTrack.Clips = RemoveClip(VideoTrack.Clips, cid);
                    if (inMic) MicTrack.Clips = RemoveClip(MicTrack.Clips, cid);
                    if (inSys) SysTrack.Clips = RemoveClip(SysTrack.Clips, cid);
                }
            }

            ClearClipSelection();
            SelectedTrackType = null;
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

    private static List<ClipSegment> RemoveClip(List<ClipSegment> clips, string clipId)
    {
        return clips.Where(c => c.Id != clipId).ToList();
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
        else if (string.Equals(trackType, "click", StringComparison.OrdinalIgnoreCase))
            CursorClickSound = !CursorClickSound;

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

        var curPt = ZoomEngineService.GetInterpolatedCursorPosition(MouseMoves, CurrentTimeSec);
        double natW = VideoWidth > 0 ? VideoWidth : 1920.0;
        double natH = VideoHeight > 0 ? VideoHeight : 1080.0;
        double targetX = curPt.HasValue ? curPt.Value.X : (natW / 2.0);
        double targetY = curPt.HasValue ? curPt.Value.Y : (natH / 2.0);

        var newZoom = new ZoomEffect
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = $"Zoom {ZoomEffects.Count + 1}",
            StartTime = Math.Round(CurrentTimeSec, 2),
            Duration = 2.0,
            TargetX = Math.Round(targetX, 1),
            TargetY = Math.Round(targetY, 1),
            Scale = Math.Clamp(DefaultZoomScale > 1.0 ? DefaultZoomScale : 1.5, 1.0, 2.2),
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
        manifest.Timeline.ClickTrack = ClickTrack;

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
    /// cursorX/cursorY: Kaynak video koordinatlarındaki fare konumu (fare takibi için).
    /// </summary>
    public ZoomEngineService.ActiveZoomState? GetCurrentZoom(double cursorX = -1, double cursorY = -1)
    {
        if ((cursorX < 0 || cursorY < 0) && MouseMoves != null && MouseMoves.Count > 0)
        {
            var pt = ZoomEngineService.GetInterpolatedCursorPosition(MouseMoves, CurrentTimeSec);
            if (pt.HasValue)
            {
                cursorX = pt.Value.X;
                cursorY = pt.Value.Y;
            }
        }

        double defW = VideoWidth > 0 ? VideoWidth : 1920.0;
        double defH = VideoHeight > 0 ? VideoHeight : 1080.0;

        return _zoomEngineService.GetActiveZoomAtTime(
            ZoomEffects.ToList(),
            CurrentTimeSec,
            defaultCenterX: defW / 2.0,
            defaultCenterY: defH / 2.0,
            cursorX: cursorX,
            cursorY: cursorY,
            moves: MouseMoves);
    }

    /// <summary>
    /// Projedeki fare tıklamalarına göre akıllı zoom efektlerini birleştirilmiş ve akıcı biçimde baştan hesaplar.
    /// </summary>
    public void AutoRegenerateZoomEffects()
    {
        if (MouseClicks == null || MouseClicks.Count == 0) return;

        PushHistory();
        double defW = VideoWidth > 0 ? VideoWidth : 1920.0;
        double defH = VideoHeight > 0 ? VideoHeight : 1080.0;

        var generated = _zoomEngineService.GenerateZoomEffectsFromClicks(
            MouseClicks,
            moves: MouseMoves,
            autoZoomMode: "smooth",
            defaultScale: Math.Clamp(DefaultZoomScale > 1.0 ? DefaultZoomScale : 1.5, 1.0, 2.2),
            maxVideoDurationSec: TotalDurationSec,
            defaultCenterX: defW / 2.0,
            defaultCenterY: defH / 2.0);

        ZoomEffects.Clear();
        foreach (var z in generated)
        {
            z.Scale = Math.Clamp(z.Scale, 1.0, 2.2);
            ZoomEffects.Add(z);
        }

        SelectedZoomEffect = ZoomEffects.FirstOrDefault();
        SaveProject();
    }

    private void NotifyAllProperties()
    {
        OnPropertyChanged(nameof(CursorSmoothing));
        OnPropertyChanged(nameof(CursorSize));
        OnPropertyChanged(nameof(CursorVisible));
        OnPropertyChanged(nameof(CursorStyle));
        OnPropertyChanged(nameof(ClickEffect));
        OnPropertyChanged(nameof(CursorClickSound));
        OnPropertyChanged(nameof(CursorClickSoundFile));
        OnPropertyChanged(nameof(CursorClickVolume));
        OnPropertyChanged(nameof(HideCursorWhenIdle));
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
        OnPropertyChanged(nameof(MicMuted));
        OnPropertyChanged(nameof(VolumeEnhancement));
        OnPropertyChanged(nameof(AudioNoiseReduction));
        OnPropertyChanged(nameof(VocalEnhancement));
        OnPropertyChanged(nameof(VocalEnhancementAmount));
        OnPropertyChanged(nameof(Padding));
        OnPropertyChanged(nameof(Inset));
        OnPropertyChanged(nameof(Roundness));
        OnPropertyChanged(nameof(Shadow));
        OnPropertyChanged(nameof(FixedZoomPart));
        OnPropertyChanged(nameof(CanvasPreset));
        OnPropertyChanged(nameof(ZoomInMotionBlur));
        OnPropertyChanged(nameof(ZoomInMotionBlurAmount));
        OnPropertyChanged(nameof(ScreenMotionBlurAmount));
        OnPropertyChanged(nameof(CursorMotionBlurAmount));
        OnPropertyChanged(nameof(ZoomPanMovementType));
        OnPropertyChanged(nameof(CursorMovementType));
        OnPropertyChanged(nameof(ShortcutKeyStyle));
        OnPropertyChanged(nameof(ShortcutFontColor));
        OnPropertyChanged(nameof(ShortcutBgColor));
        OnPropertyChanged(nameof(ShortcutBgOpacity));
        OnPropertyChanged(nameof(ShortcutSize));
        OnPropertyChanged(nameof(ShortcutPosition));
        OnPropertyChanged(nameof(DisplaySingleShortcutKey));
    }
}
