namespace ScreenPowerPro.Models;

public readonly record struct VisualElementLayout(
    ITimelineElement Element,
    double X, 
    double Width,
    bool IsPartialLeft,
    bool IsPartialRight);
