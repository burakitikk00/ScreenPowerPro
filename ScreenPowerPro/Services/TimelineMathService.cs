using System;
using System.Collections.Generic;

namespace ScreenPowerPro.Services;

/// <summary>
/// Profesyonel NLE (Non-Linear Editing) zaman ve piksel dönüşümlerini,
/// manyetik yapışma (Magnetic Snapping) hesaplamalarını sağlayan yardımcı servis.
/// </summary>
public class TimelineMathService
{
    public const double DefaultLeftOffset = 40.0;
    public const double DefaultSnapThresholdPixels = 15.0;

    /// <summary>
    /// Verilen zamanı (saniye) timeline üzerindeki piksel koordinatına çevirir.
    /// </summary>
    public static double TimeToPixel(double timeSeconds, double scale, double leftOffset = DefaultLeftOffset)
    {
        return leftOffset + (Math.Max(0, timeSeconds) * scale);
    }

    /// <summary>
    /// Verilen piksel koordinatını timeline zamanına (saniye) çevirir.
    /// </summary>
    public static double PixelToTime(double pixelX, double scale, double leftOffset = DefaultLeftOffset)
    {
        if (scale <= 0) return 0;
        return Math.Max(0, (pixelX - leftOffset) / scale);
    }

    /// <summary>
    /// Verilen süreyi (saniye) genişlik pikseline çevirir.
    /// </summary>
    public static double DurationToWidth(double durationSeconds, double scale, double minWidth = 20.0)
    {
        return Math.Max(minWidth, Math.Max(0, durationSeconds) * scale);
    }

    /// <summary>
    /// Verilen piksel genişliğini süreye (saniye) çevirir.
    /// </summary>
    public static double WidthToDuration(double widthPixels, double scale)
    {
        if (scale <= 0) return 0;
        return Math.Max(0, widthPixels / scale);
    }

    /// <summary>
    /// Hedef piksel konumunun, verilen yapışma noktalarına (snap points) yakın olup olmadığını kontrol eder.
    /// Eşik mesafe (varsayılan 15px) içinde bir nokta varsa en yakın noktaya yapışır.
    /// </summary>
    /// <param name="candidatePixel">Aday piksel konumu</param>
    /// <param name="snapPointPixels">Yapışılabilecek piksel noktaları listesi</param>
    /// <param name="thresholdPixels">Yapışma eşiği (piksel, örn: 12-15px)</param>
    /// <returns>(Yapışma gerçekleşti mi, Yapışılan piksel konumu)</returns>
    public static (bool Snapped, double SnappedPixel) FindSnapPixel(
        double candidatePixel,
        IEnumerable<double> snapPointPixels,
        double thresholdPixels = DefaultSnapThresholdPixels)
    {
        double closestDist = double.MaxValue;
        double bestSnap = candidatePixel;
        bool snapped = false;

        foreach (var target in snapPointPixels)
        {
            double dist = Math.Abs(candidatePixel - target);
            if (dist <= thresholdPixels && dist < closestDist)
            {
                closestDist = dist;
                bestSnap = target;
                snapped = true;
            }
        }

        return (snapped, snapped ? bestSnap : candidatePixel);
    }

    /// <summary>
    /// Zaman bazında yapışma kontrolü yapar.
    /// </summary>
    public static (bool Snapped, double SnappedTime) FindSnapTime(
        double candidateTime,
        IEnumerable<double> snapPointTimes,
        double scale,
        double thresholdPixels = DefaultSnapThresholdPixels)
    {
        if (scale <= 0) return (false, candidateTime);
        double thresholdSeconds = thresholdPixels / scale;

        double closestDist = double.MaxValue;
        double bestSnap = candidateTime;
        bool snapped = false;

        foreach (var target in snapPointTimes)
        {
            double dist = Math.Abs(candidateTime - target);
            if (dist <= thresholdSeconds && dist < closestDist)
            {
                closestDist = dist;
                bestSnap = target;
                snapped = true;
            }
        }

        return (snapped, snapped ? bestSnap : candidateTime);
    }
}
