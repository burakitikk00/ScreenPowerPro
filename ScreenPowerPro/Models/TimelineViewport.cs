using System;

namespace ScreenPowerPro.Models;

public sealed class TimelineViewport
{
    public double PixelsPerSecond { get; init; }
    public TimeSpan VisibleStart { get; init; }
    public TimeSpan VisibleEnd { get; init; }
    public double ViewWidth { get; init; }
}
