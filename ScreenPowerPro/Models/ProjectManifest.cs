using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ScreenPowerPro.Models;

public class ProjectManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = "Yeni Kayıt";

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");

    [JsonPropertyName("videoPath")]
    public string VideoPath { get; set; } = string.Empty;

    [JsonPropertyName("micAudioPath")]
    public string? MicAudioPath { get; set; }

    [JsonPropertyName("systemAudioPath")]
    public string? SystemAudioPath { get; set; }

    [JsonPropertyName("timeline")]
    public TimelineModel Timeline { get; set; } = new();

    [JsonPropertyName("metadata")]
    public RecordingMetadata Metadata { get; set; } = new();
}

public class TimelineModel
{
    [JsonPropertyName("zoomEffects")]
    public List<ZoomEffect> ZoomEffects { get; set; } = new();

    [JsonPropertyName("settings")]
    public TimelineSettings Settings { get; set; } = new();
}

public class ZoomEffect
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Zoom";

    [JsonPropertyName("startTime")]
    public double StartTime { get; set; } // seconds

    [JsonPropertyName("duration")]
    public double Duration { get; set; } = 2.0; // seconds

    [JsonPropertyName("targetX")]
    public double TargetX { get; set; }

    [JsonPropertyName("targetY")]
    public double TargetY { get; set; }

    [JsonPropertyName("scale")]
    public double Scale { get; set; } = 1.5; // 1.2x, 1.5x, 2.0x

    [JsonPropertyName("easing")]
    public string Easing { get; set; } = "ease-in-out"; // ease-in-out, ease-out, linear
}

public class TimelineSettings
{
    [JsonPropertyName("cursorSmoothing")]
    public string CursorSmoothing { get; set; } = "medium"; // off, slow, medium, fast

    [JsonPropertyName("motionBlur")]
    public bool MotionBlur { get; set; } = true;

    [JsonPropertyName("watermark")]
    public bool Watermark { get; set; } = false;

    [JsonPropertyName("showKeystrokes")]
    public bool ShowKeystrokes { get; set; } = true;

    [JsonPropertyName("aspectRatio")]
    public string AspectRatio { get; set; } = "16:9"; // 16:9, 9:16, 1:1, 4:3

    [JsonPropertyName("backgroundStyle")]
    public string BackgroundStyle { get; set; } = "gradient-1"; // gradient-1, gradient-2, dark, blur
}

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

    [JsonPropertyName("hasMicAudio")]
    public bool HasMicAudio { get; set; }

    [JsonPropertyName("hasSystemAudio")]
    public bool HasSystemAudio { get; set; }
}
