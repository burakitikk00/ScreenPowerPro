using System;
using System.IO;
using System.Text.Json.Serialization;

namespace ScreenPowerPro.Models;

public class AppSettings
{
    [JsonPropertyName("projectSaveLocation")]
    public string ProjectSaveLocation { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "ScreenPowerPro Projects"
    );

    [JsonPropertyName("exportLocation")]
    public string ExportLocation { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "ScreenPowerPro Exports"
    );

    [JsonPropertyName("autoZoom")]
    public bool AutoZoom { get; set; } = true;

    [JsonPropertyName("hideDesktopIcons")]
    public bool HideDesktopIcons { get; set; } = false;

    [JsonPropertyName("hideTaskbar")]
    public bool HideTaskbar { get; set; } = false;

    [JsonPropertyName("hideMouseCursor")]
    public bool HideMouseCursor { get; set; } = false;

    [JsonPropertyName("resolution")]
    public string Resolution { get; set; } = "1080p"; // 720p, 1080p, 4K

    [JsonPropertyName("countdownSeconds")]
    public int CountdownSeconds { get; set; } = 3;

    [JsonPropertyName("fps")]
    public int Fps { get; set; } = 60;

    [JsonPropertyName("exportFormat")]
    public string ExportFormat { get; set; } = "mp4";

    [JsonPropertyName("micAudioEnabled")]
    public bool MicAudioEnabled { get; set; } = true;

    [JsonPropertyName("systemAudioEnabled")]
    public bool SystemAudioEnabled { get; set; } = true;

    [JsonPropertyName("cameraEnabled")]
    public bool CameraEnabled { get; set; } = false;

    [JsonPropertyName("selectedMicDevice")]
    public string? SelectedMicDevice { get; set; }

    [JsonPropertyName("selectedCameraDevice")]
    public string? SelectedCameraDevice { get; set; }

    [JsonPropertyName("excludeAppFromRecording")]
    public bool ExcludeAppFromRecording { get; set; } = true; // Uses WDA_EXCLUDEFROMCAPTURE
}
