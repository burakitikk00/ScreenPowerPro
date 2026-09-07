using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ScreenPowerPro.Models;

/// <summary>
/// ScreenPowerPro proje manifestosu. Kayıt dosyaları, zaman çizelgesi, zoom efektleri,
/// parça (track) segmentleri ve proje ayarlarının tamamını saklar.
/// </summary>
public class ProjectManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = "Yeni Kayıt";

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");

    /// <summary>
    /// Video dosyasının proje dizinine göreceli konumu (örn: "./recording/display-0.mp4")
    /// </summary>
    [JsonPropertyName("videoPath")]
    public string VideoPath { get; set; } = string.Empty;

    /// <summary>
    /// Mikrofon ses kaydı konumu
    /// </summary>
    [JsonPropertyName("micAudioPath")]
    public string? MicAudioPath { get; set; }

    /// <summary>
    /// Sistem ses kaydı konumu
    /// </summary>
    [JsonPropertyName("systemAudioPath")]
    public string? SystemAudioPath { get; set; }

    /// <summary>
    /// Zaman çizelgesi verileri (zoom efektleri, parçalar ve görsel ayarlar)
    /// </summary>
    [JsonPropertyName("timeline")]
    public TimelineModel Timeline { get; set; } = new();

    /// <summary>
    /// Kayıt anındaki video/ses meta verileri
    /// </summary>
    [JsonPropertyName("metadata")]
    public RecordingMetadata Metadata { get; set; } = new();
}

/// <summary>
/// Zaman çizelgesi ana yapısı. Parçaları (video, mikrofon, sistem sesi)
/// ve zoom efektlerini bir arada tutar.
/// </summary>
public class TimelineModel
{
    [JsonPropertyName("zoomEffects")]
    public List<ZoomEffect> ZoomEffects { get; set; } = new();

    [JsonPropertyName("videoTrack")]
    public TrackState VideoTrack { get; set; } = new();

    [JsonPropertyName("micTrack")]
    public TrackState MicTrack { get; set; } = new();

    [JsonPropertyName("sysTrack")]
    public TrackState SysTrack { get; set; } = new();

    [JsonPropertyName("settings")]
    public TimelineSettings Settings { get; set; } = new();
}

/// <summary>
/// Bir zaman çizelgesi parçasının (Video, Mic, Sys) durumunu ve klip parçalarını tutar.
/// </summary>
public class TrackState
{
    [JsonPropertyName("offset")]
    public double Offset { get; set; } = 0;

    [JsonPropertyName("trimStart")]
    public double TrimStart { get; set; } = 0;

    [JsonPropertyName("trimEnd")]
    public double TrimEnd { get; set; } = 0;

    [JsonPropertyName("muted")]
    public bool Muted { get; set; } = false;

    [JsonPropertyName("clips")]
    public List<ClipSegment> Clips { get; set; } = new();
}

/// <summary>
/// Klip bölme (cut/split) ve silme işlemlerinde kullanılan klip parçası.
/// Kaynak dosyadaki başlangıç-bitiş ve zaman çizelgesindeki öteleme konumunu tanımlar.
/// </summary>
public class ClipSegment
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("sourceStart")]
    public double SourceStart { get; set; }

    [JsonPropertyName("sourceEnd")]
    public double SourceEnd { get; set; }

    [JsonPropertyName("trackOffset")]
    public double TrackOffset { get; set; }
}

/// <summary>
/// Video üzerine uygulanan zoom veya pan efekti modeli.
/// </summary>
public class ZoomEffect
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Zoom";

    [JsonPropertyName("startTime")]
    public double StartTime { get; set; } // saniye

    [JsonPropertyName("duration")]
    public double Duration { get; set; } = 2.0; // saniye

    [JsonPropertyName("targetX")]
    public double TargetX { get; set; }

    [JsonPropertyName("targetY")]
    public double TargetY { get; set; }

    [JsonPropertyName("scale")]
    public double Scale { get; set; } = 1.5; // 1.2x, 1.5x, 2.0x vs.

    [JsonPropertyName("easing")]
    public string Easing { get; set; } = "ease-in-out"; // ease-in-out, instant, linear
}

/// <summary>
/// Düzenleme görünümü ve dışa aktarımda kullanılan görsel/işitsel ayarlar.
/// Electron mimarisindeki TimelineSettings'in 16+ alanıyla tam uyumludur.
/// </summary>
public class TimelineSettings
{
    // --- Fare ve İmleç Ayarları ---
    [JsonPropertyName("cursorSmoothing")]
    public string CursorSmoothing { get; set; } = "medium"; // off, slow, medium, fast

    [JsonPropertyName("cursorSize")]
    public double CursorSize { get; set; } = 100; // %100

    [JsonPropertyName("cursorVisible")]
    public bool CursorVisible { get; set; } = true;

    [JsonPropertyName("cursorStyle")]
    public string CursorStyle { get; set; } = "default"; // default, highlight, spotlight

    [JsonPropertyName("clickEffect")]
    public string ClickEffect { get; set; } = "default"; // default, ripple, circle, none

    [JsonPropertyName("cursorClickSound")]
    public bool CursorClickSound { get; set; } = false;

    [JsonPropertyName("hideCursorWhenIdle")]
    public bool HideCursorWhenIdle { get; set; } = false;

    // --- Hareket Bulanıklığı ve Efektler ---
    [JsonPropertyName("motionBlur")]
    public bool MotionBlur { get; set; } = false;

    [JsonPropertyName("motionBlurAmount")]
    public double MotionBlurAmount { get; set; } = 50; // 0 - 100

    [JsonPropertyName("defaultZoomScale")]
    public double DefaultZoomScale { get; set; } = 1.5;

    [JsonPropertyName("videoSpeed")]
    public double VideoSpeed { get; set; } = 1.0; // 0.5, 1.0, 1.25, 1.5, 2.0

    // --- Görsel / Tuval (Canvas) Ayarları ---
    [JsonPropertyName("canvasBackground")]
    public string CanvasBackground { get; set; } = "#1e1f27";

    [JsonPropertyName("backgroundOpacity")]
    public double BackgroundOpacity { get; set; } = 100; // 0 - 100

    [JsonPropertyName("aspectRatio")]
    public string AspectRatio { get; set; } = "16:9"; // 16:9, 9:16, 1:1, 4:3

    [JsonPropertyName("backgroundStyle")]
    public string BackgroundStyle { get; set; } = "gradient-1"; // gradient-1, gradient-2, dark, blur

    // --- Filigran (Watermark) & Kısayol Tuşları ---
    [JsonPropertyName("watermark")]
    public bool Watermark { get; set; } = false;

    [JsonPropertyName("watermarkText")]
    public string WatermarkText { get; set; } = "ScreenPowerPro";

    [JsonPropertyName("showShortcutKeys")]
    public bool ShowShortcutKeys { get; set; } = true;

    [JsonPropertyName("showKeystrokes")]
    public bool ShowKeystrokes
    {
        get => ShowShortcutKeys;
        set => ShowShortcutKeys = value;
    }

    // --- Kamera Overlay Ayarları ---
    [JsonPropertyName("cameraVisible")]
    public bool CameraVisible { get; set; } = true;

    [JsonPropertyName("cameraShape")]
    public string CameraShape { get; set; } = "circle"; // circle, square, rounded

    [JsonPropertyName("cameraSize")]
    public double CameraSize { get; set; } = 100; // % cinsinden boyut

    // --- Ses Ayarları ---
    [JsonPropertyName("micVolume")]
    public double MicVolume { get; set; } = 100; // % ses seviyesi

    [JsonPropertyName("sysVolume")]
    public double SysVolume { get; set; } = 100; // % ses seviyesi

    [JsonPropertyName("micMuted")]
    public bool MicMuted { get; set; } = false;

    [JsonPropertyName("volumeEnhancement")]
    public double VolumeEnhancement { get; set; } = 1.0;

    [JsonPropertyName("audioNoiseReduction")]
    public bool AudioNoiseReduction { get; set; } = false;

    [JsonPropertyName("vocalEnhancement")]
    public bool VocalEnhancement { get; set; } = false;

    [JsonPropertyName("vocalEnhancementAmount")]
    public int VocalEnhancementAmount { get; set; } = 48;

    // --- Tuval (Canvas) ve Çerçeve Detayları ---
    [JsonPropertyName("padding")]
    public int Padding { get; set; } = 5;

    [JsonPropertyName("inset")]
    public int Inset { get; set; } = 0;

    [JsonPropertyName("roundness")]
    public int Roundness { get; set; } = 4;

    [JsonPropertyName("shadow")]
    public int Shadow { get; set; } = 100;

    [JsonPropertyName("fixedZoomPart")]
    public bool FixedZoomPart { get; set; } = false;

    [JsonPropertyName("canvasPreset")]
    public string CanvasPreset { get; set; } = "Default";

    // --- Hareket Bulanıklığı ve Hız Detayları ---
    [JsonPropertyName("zoomInMotionBlur")]
    public bool ZoomInMotionBlur { get; set; } = true;

    [JsonPropertyName("zoomInMotionBlurAmount")]
    public double ZoomInMotionBlurAmount { get; set; } = 50;

    [JsonPropertyName("screenMotionBlurAmount")]
    public double ScreenMotionBlurAmount { get; set; } = 50;

    [JsonPropertyName("cursorMotionBlurAmount")]
    public double CursorMotionBlurAmount { get; set; } = 50;

    [JsonPropertyName("zoomPanMovementType")]
    public string ZoomPanMovementType { get; set; } = "Slow";

    [JsonPropertyName("cursorMovementType")]
    public string CursorMovementType { get; set; } = "Medium";

    // --- Kısayol Tuşları Görünüm Detayları ---
    [JsonPropertyName("shortcutKeyStyle")]
    public string ShortcutKeyStyle { get; set; } = "filled";

    [JsonPropertyName("shortcutFontColor")]
    public string ShortcutFontColor { get; set; } = "#FFFFFF";

    [JsonPropertyName("shortcutBgColor")]
    public string ShortcutBgColor { get; set; } = "#000000";

    [JsonPropertyName("shortcutBgOpacity")]
    public int ShortcutBgOpacity { get; set; } = 80;

    [JsonPropertyName("shortcutSize")]
    public int ShortcutSize { get; set; } = 24;

    [JsonPropertyName("shortcutPosition")]
    public string ShortcutPosition { get; set; } = "bottom-center";

    [JsonPropertyName("displaySingleShortcutKey")]
    public bool DisplaySingleShortcutKey { get; set; } = false;
}

/// <summary>
/// Kayıt oturumu meta verileri (çözünürlük, kare hızı, toplam süre ve ses kanalları).
/// </summary>
public class RecordingMetadata
{
    [JsonPropertyName("width")]
    public int Width { get; set; } = 1920;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 1080;

    [JsonPropertyName("fps")]
    public int Fps { get; set; } = 60;

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("originX")]
    public int OriginX { get; set; } = 0;

    [JsonPropertyName("originY")]
    public int OriginY { get; set; } = 0;

    [JsonPropertyName("hasMicAudio")]
    public bool HasMicAudio { get; set; }

    [JsonPropertyName("hasSystemAudio")]
    public bool HasSystemAudio { get; set; }
}

/// <summary>
/// Kaydedilmiş bir projenin özet bilgileri (Kütüphane ve Son Kayıtlar listesi için).
/// </summary>
public partial class ProjectInfo : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string VideoPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public double DurationSeconds { get; set; }
    public int ZoomCount { get; set; }
    public string ZoomBadgeText => $"{ZoomCount} Zoom";

    public string FormattedDuration => TimeSpan.FromSeconds(DurationSeconds).ToString(@"mm\:ss");
    public string FormattedDate => CreatedAt.ToString("dd.MM.yyyy HH:mm");

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _editButtonText = "Düzenle";

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _revealTooltip = "Klasörde Aç";

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _deleteTooltip = "Sil";
}
