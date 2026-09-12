using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Services;

public sealed class TimelineZoomController
{
    private readonly TimelineVirtualizer _virtualizer;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource _zoomCts = new();
    
    // Config
    private const double MinPps = 10.0;
    private const double MaxPps = 2000.0;

    public TimelineZoomController(TimelineVirtualizer virtualizer, DispatcherQueue dispatcherQueue)
    {
        _virtualizer = virtualizer;
        _dispatcherQueue = dispatcherQueue;
    }

    /// <summary>
    /// Processes a scroll wheel delta asynchronously and dispatches the layout update.
    /// </summary>
    public void OnZoomDelta(
        double delta,
        double currentPps,
        double viewWidth,
        TimeSpan visibleStart,
        IReadOnlyList<ITimelineElement> elements,
        Action<TimelineViewport, IReadOnlyList<VisualElementLayout>> onLayoutReady)
    {
        // Cancel the previous calculation if the user is scrolling rapidly
        _zoomCts.Cancel();
        _zoomCts = new CancellationTokenSource();
        var ct = _zoomCts.Token;

        // Determine new zoom scale
        double factor = 1.0 + (delta * 0.1);
        double newPps = Math.Clamp(currentPps * factor, MinPps, MaxPps);

        Task.Run(() =>
        {
            if (ct.IsCancellationRequested) return;

            // Pure math operations off the UI thread
            double durationSeconds = viewWidth / newPps;
            var visibleEnd = visibleStart.Add(TimeSpan.FromSeconds(durationSeconds));

            var viewport = new TimelineViewport
            {
                PixelsPerSecond = newPps,
                VisibleStart = visibleStart,
                VisibleEnd = visibleEnd,
                ViewWidth = viewWidth
            };

            var layouts = _virtualizer.ComputeLayout(elements, viewport);

            if (ct.IsCancellationRequested) return;

            // Push the immutable snapshot to the UI thread
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    onLayoutReady(viewport, layouts);
                }
            });

        }, ct);
    }
}
