using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Views.Controls;

public class VirtualizingCanvas : Panel
{
    private IReadOnlyList<VisualElementLayout> _currentLayouts;

    public VirtualizingCanvas()
    {
        _currentLayouts = new List<VisualElementLayout>();
    }

    /// <summary>
    /// Applies a new layout snapshot calculated by the background thread.
    /// This immediately invalidates measure and arrange to render the visible elements.
    /// </summary>
    public void ApplyLayout(IReadOnlyList<VisualElementLayout> layouts)
    {
        _currentLayouts = layouts;
        InvalidateMeasure();
    }

    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
    {
        // In a real VirtualizingCanvas with a recycle pool, we would map the layouts to
        // UI elements from the Children collection (or create new ones).
        // For simplicity in this architectural blueprint, we assume Children are bound 
        // to the layouts somehow (e.g. via DataTemplate) or created dynamically.
        
        foreach (var child in Children)
        {
            if (child is FrameworkElement fe)
            {
                fe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, availableSize.Height));
            }
        }

        return availableSize;
    }

    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
    {
        // If we strictly follow the snapshot, we look at the UI elements in Children
        // and arrange them according to their corresponding VisualElementLayout.
        // Assuming we map them by index for this basic implementation:
        
        for (int i = 0; i < Children.Count && i < _currentLayouts.Count; i++)
        {
            var child = Children[i];
            var layout = _currentLayouts[i];
            
            if (child is FrameworkElement fe)
            {
                // Arrange the child at the pre-calculated X and Width
                child.Arrange(new Windows.Foundation.Rect(layout.X, 0, layout.Width, fe.DesiredSize.Height));
            }
        }

        return finalSize;
    }
}
