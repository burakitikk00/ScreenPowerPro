using System;
using System.Collections.Generic;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Services;

public sealed class TimelineVirtualizer
{
    /// <summary>
    /// Computes the layout of timeline elements that are visible within the given viewport.
    /// This is a pure function and safe to call from background threads.
    /// </summary>
    public IReadOnlyList<VisualElementLayout> ComputeLayout(
        IReadOnlyList<ITimelineElement> allElements,
        TimelineViewport viewport)
    {
        var layouts = new List<VisualElementLayout>(allElements.Count); // Pre-allocate with max possible capacity for speed, or a tuned heuristic.

        foreach (var element in allElements)
        {
            if (element.IntersectsWith(viewport))
            {
                // Calculate pixel X and Width relative to the viewport's VisibleStart.
                // Elements that start before the viewport will have negative X.
                double x = TimeToPixel(element.Start, viewport);
                double endX = TimeToPixel(element.End, viewport);
                double width = endX - x;

                bool isPartialLeft = element.Start < viewport.VisibleStart;
                bool isPartialRight = element.End > viewport.VisibleEnd;

                layouts.Add(new VisualElementLayout(element, x, width, isPartialLeft, isPartialRight));
            }
        }

        return layouts;
    }

    /// <summary>
    /// Maps a TimeSpan to a pixel coordinate within the viewport.
    /// This uses a fast, non-allocating math path.
    /// </summary>
    public double TimeToPixel(TimeSpan t, TimelineViewport vp)
    {
        return (t - vp.VisibleStart).TotalSeconds * vp.PixelsPerSecond;
    }
}
