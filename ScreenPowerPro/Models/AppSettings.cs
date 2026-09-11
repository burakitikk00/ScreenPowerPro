using System;
using System.IO;
using System.Text.Json.Serialization;

namespace ScreenPowerPro.Models;

/// <summary>
/// Özel bölge kırpma koordinatları ve boyutları.
/// </summary>
public class CropBounds
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}

/// <summary>
/// ScreenPowerPro genel uygulama yapılandırması ve kullanıcı tercihleri modeli.
/// Electron mimarisindeki AppSettings yapısıyla tam uyumludur.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Kaydedilen projelerin saklanacağı varsayılan dizin.
    /// </summary>
    [JsonPropertyName("projectSaveLocation")]
    public string ProjectSaveLocation { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "ScreenPowerPro Projects"
    );

    /// <summary>
    /// Render edilen nihai videoların dışa aktarılacağı dizin.
    /// </summary>
    [JsonPropertyName("exportLocation")]
    public string ExportLocation { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "ScreenPowerPro Exports"
    );

    /// <summary>
    /// Uygulama arayüz dili: "tr" (Türkçe) veya "en" (English).
    /// </summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "tr";

    /// <summary>
    /// Otomatik zoom modu: "none" (kapalı), "smooth" (yumuşak geçişli), "instant" (anlık sıçrama).
    /// </summary>
    [JsonPropertyName("autoZoomMode")]
    public string AutoZoomMode { get; set; } = "smooth";

    /// <summary>
    /// UI ve ayarlar için Zoom Effect: "2D Zoom", "3D Motion", "None"
    /// </summary>
    [JsonPropertyName("zoomEffect")]
    public string ZoomEffect
    {
        get => AutoZoomMode switch
        {
            "smooth" => "2D Zoom",
            "instant" => "3D Motion",
            "none" => "None",
            _ => "2D Zoom"
        };
        set
        {
            AutoZoomMode = value switch
            {
                "2D Zoom" => "smooth",
                "3D Motion" => "instant",
                "None" => "none",
                _ => "smooth"
            };
        }
    }

    /// <summary>
    /// Geriye dönük uyumluluk için boolean AutoZoom özelliği.
    /// </summary>
    [JsonPropertyName("autoZoom")]
    public bool AutoZoom
    {
        get => !string.Equals(AutoZoomMode, "none", StringComparison.OrdinalIgnoreCase);
        set => AutoZoomMode = value ? "smooth" : "none";
    }

    /// <summary>
    /// Windows ile birlikte otomatik başlatma tercihi.
    /// </summary>
    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; } = false;

    /// <summary>
    /// Editör açıldığında videonun otomatik oynatılması tercihi.
    /// </summary>
    [JsonPropertyName("autoPlayVideo")]
    public bool AutoPlayVideo { get; set; } = true;

    /// <summary>
    /// Kayıt kalitesi: "Ultra", "High", "Medium", "Low"
    /// </summary>
    [JsonPropertyName("recordingQuality")]
    public string RecordingQuality { get; set; } = "High";

    /// <summary>
    /// Kaydı Başlat/Durdur kısayolu (örn. F9).
    /// </summary>
    [JsonPropertyName("shortcutStartStop")]
    public string ShortcutStartStop { get; set; } = "F9";

    /// <summary>
    /// Kaydı Duraklat/Devam Et kısayolu (örn. F10).
    /// </summary>
    [JsonPropertyName("shortcutPause")]
    public string ShortcutPause { get; set; } = "F10";

    /// <summary>
    /// Ekran görüntüsü alma kısayolu (örn. F11).
    /// </summary>
    [JsonPropertyName("shortcutScreenshot")]
    public string ShortcutScreenshot { get; set; } = "F11";

    /// <summary>
    /// Kayıt esnasında masaüstü simgelerini gizleme tercihi.
    /// </summary>
    [JsonPropertyName("hideDesktopIcons")]
    public bool HideDesktopIcons { get; set; } = false;

    /// <summary>
    /// Kayıt esnasında Windows görev çubuğunu gizleme tercihi.
    /// </summary>
    [JsonPropertyName("hideTaskbar")]
    public bool HideTaskbar { get; set; } = false;

    /// <summary>
    /// Ham video kaydında fare imlecini gizleme (imleç sonradan efektli çizdirilecekse).
    /// </summary>
    [JsonPropertyName("hideMouseCursor")]
    public bool HideMouseCursor { get; set; } = false;

    /// <summary>
    /// Kayıt çözünürlüğü: "720p", "1080p", "4K"
    /// </summary>
    [JsonPropertyName("resolution")]
    public string Resolution { get; set; } = "1080p";

    /// <summary>
    /// Kayıt öncesi görsel geri sayım süresi (saniye): 0 (kapalı), 3, 5, 10
    /// </summary>
    [JsonPropertyName("countdownSeconds")]
    public int CountdownSeconds { get; set; } = 3;

    /// <summary>
    /// Hedef kare hızı (FPS): 30, 60
    /// </summary>
    [JsonPropertyName("fps")]
    public int Fps { get; set; } = 60;

    /// <summary>
    /// Dışa aktarım dosya formatı: "mp4", "webm"
    /// </summary>
    [JsonPropertyName("exportFormat")]
    public string ExportFormat { get; set; } = "mp4";

    /// <summary>
    /// Dışa aktarım çözünürlüğü: "720p", "1080p", "4k"
    /// </summary>
    [JsonPropertyName("exportResolution")]
    public string ExportResolution { get; set; } = "1080p";

    /// <summary>
    /// Mikrofon sesi kaydı aktif mi?
    /// </summary>
    [JsonPropertyName("micAudioEnabled")]
    public bool MicAudioEnabled { get; set; } = true;

    /// <summary>
    /// Bilgisayar sistem sesi (loopback) kaydı aktif mi?
    /// </summary>
    [JsonPropertyName("systemAudioEnabled")]
    public bool SystemAudioEnabled { get; set; } = true;

    /// <summary>
    /// Kamera (webcam) overlay penceresi açık mı?
    /// </summary>
    [JsonPropertyName("cameraEnabled")]
    public bool CameraEnabled { get; set; } = false;

    /// <summary>
    /// Seçilen mikrofon aygıt kimliği veya adı.
    /// </summary>
    [JsonPropertyName("selectedMicDevice")]
    public string? SelectedMicDevice { get; set; }

    /// <summary>
    /// Seçilen hoparlör / ses çıkış aygıtı.
    /// </summary>
    [JsonPropertyName("selectedSpeakerDevice")]
    public string? SelectedSpeakerDevice { get; set; }

    /// <summary>
    /// Seçilen web kamerası aygıtı.
    /// </summary>
    [JsonPropertyName("selectedCameraDevice")]
    public string? SelectedCameraDevice { get; set; }

    /// <summary>
    /// Özel bölge kaydı için belirlenen kırpma sınırları.
    /// </summary>
    [JsonPropertyName("customCropBounds")]
    public CropBounds? CustomCropBounds { get; set; }

    /// <summary>
    /// Yalnızca belirli uygulamaların seslerini kaydetme modu aktif mi?
    /// </summary>
    [JsonPropertyName("onlyAppAudioEnabled")]
    public bool OnlyAppAudioEnabled { get; set; } = false;

    /// <summary>
    /// Only App Audio modunda sesi kaydedilecek seçili işlem (process) kimlikleri veya isimleri.
    /// </summary>
    [JsonPropertyName("selectedAppAudioProcesses")]
    public List<string> SelectedAppAudioProcesses { get; set; } = new();

    /// <summary>
    /// Windows API aracılığıyla ScreenPowerPro penceresinin kendi kaydında görünmesini engeller.
    /// </summary>
    [JsonPropertyName("excludeAppFromRecording")]
    public bool ExcludeAppFromRecording { get; set; } = true;

    // --- Dinamik Zoom Ayarları ---
    
    /// <summary>
    /// Zoom yakınlaşma katsayısı (Örn: 1.5x, 2.0x).
    /// </summary>
    [JsonPropertyName("maxZoomRatio")]
    public double MaxZoomRatio { get; set; } = 1.5;

    /// <summary>
    /// Zoom ve Pan geçiş hızı (sn).
    /// </summary>
    [JsonPropertyName("zoomSpeed")]
    public double ZoomSpeed { get; set; } = 0.35;

    /// <summary>
    /// Zoomun ekranda kalma süresi (sn).
    /// </summary>
    [JsonPropertyName("zoomDuration")]
    public double ZoomDuration { get; set; } = 2.5;

    /// <summary>
    /// Fare video sınırlarının dışına çıkarsa zoomu iptal edip uzaklaştırma.
    /// </summary>
    [JsonPropertyName("cancelOnOutOfBounds")]
    public bool CancelOnOutOfBounds { get; set; } = true;

    /// <summary>
    /// Zoom animasyonu öncesi hazırlık/odak süresi (milisaniye).
    /// </summary>
    [JsonPropertyName("preClickAnticipationMs")]
    public int PreClickAnticipationMs { get; set; } = 200;

    /// <summary>
    /// Zoom animasyon eğrisi/matematiği (Linear, Quad-Out, Cubic-Out, Quartic-Out).
    /// </summary>
    [JsonPropertyName("zoomEasingFunction")]
    public string ZoomEasingFunction { get; set; } = "Cubic-Out";
}
