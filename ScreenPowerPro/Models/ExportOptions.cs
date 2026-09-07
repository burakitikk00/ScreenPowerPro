using System;

namespace ScreenPowerPro.Models;

/// <summary>
/// Dışa aktarma ayarları penceresinden seçilen video çözünürlük,
/// kare hızı, çıktı dosya yolu ve format parametrelerini taşıyan model.
/// </summary>
public class ExportOptions
{
    public string ProjectDir { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public int TargetWidth { get; set; } = 1920;
    public int TargetHeight { get; set; } = 1080;
    public int TargetFps { get; set; } = 60;
    public string Format { get; set; } = "mp4";
    public string ResolutionLabel { get; set; } = "1080p (1920×1080)";
}
