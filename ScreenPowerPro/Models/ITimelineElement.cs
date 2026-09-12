using System;

namespace ScreenPowerPro.Models;

public interface ITimelineElement
{
    TimeSpan Start { get; }
    TimeSpan End { get; }
    
    // Default implementation available in C# 8.0+
    bool IntersectsWith(TimelineViewport vp)
    {
        return Start < vp.VisibleEnd && End > vp.VisibleStart;
    }
}
