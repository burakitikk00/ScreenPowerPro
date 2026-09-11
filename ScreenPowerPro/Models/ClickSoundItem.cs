using System;

namespace ScreenPowerPro.Models;

/// <summary>
/// Fare tıklama sesleri koleksiyonu için model sınıfı.
/// public/mouse_click klasöründeki seslerin düzenlenmiş kullanıcı dostu bilgilerini taşır.
/// </summary>
public class ClickSoundItem
{
    /// <summary>
    /// Dosya adı (örn: universfield-computer-mouse-click-02-383961.mp3)
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Tam görünen ad (örn: Universfield - Bilgisayar Tık 02)
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Kısa başlık (örn: Bilgisayar Tık 02)
    /// </summary>
    public string ShortName { get; set; } = string.Empty;

    /// <summary>
    /// Ses tonu ve karakteri açıklaması
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Fiziksel dosya yolu
    /// </summary>
    public string FullPath { get; set; } = string.Empty;

    public override string ToString() => DisplayName;
}
