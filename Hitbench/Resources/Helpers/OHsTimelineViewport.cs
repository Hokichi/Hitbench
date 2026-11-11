namespace Hitbench.Resources.Helpers;

public static class OHsTimelineViewport
{
    public static (double Minimum, double Maximum) GetHorizontalBounds(double contentWidth, double viewportWidth,
        double edgePadding, double minimumContentX, double maximumContentX)
    {
        if (!double.IsFinite(contentWidth) || contentWidth <= 0 || !double.IsFinite(viewportWidth) || viewportWidth <= 0 ||
            !double.IsFinite(edgePadding) || edgePadding < 0 || !double.IsFinite(minimumContentX) || !double.IsFinite(maximumContentX)) return (0, 0);
        var minimum = Math.Min(0, minimumContentX) - edgePadding;
        return (minimum, Math.Max(minimum, Math.Max(contentWidth, maximumContentX) + edgePadding - viewportWidth));
    }
    public static (double Minimum, double Maximum) GetHorizontalBounds(double contentWidth, double viewportWidth, double edgePadding)
    {
        if (!double.IsFinite(contentWidth) || contentWidth <= 0 || !double.IsFinite(viewportWidth) || viewportWidth <= 0 ||
            !double.IsFinite(edgePadding) || edgePadding < 0) return (0, 0);
        return (-edgePadding, Math.Max(-edgePadding, contentWidth + edgePadding - viewportWidth));
    }

    public static double GetCenteredOffset(TimeSpan position, TimeSpan duration, double contentWidth, double viewportWidth) =>
        duration > TimeSpan.Zero && double.IsFinite(contentWidth) && contentWidth > 0 && double.IsFinite(viewportWidth) && viewportWidth > 0
            ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1) * contentWidth - viewportWidth / 2 : 0;

    public static TimeSpan? GetPositionAt(double viewportX, double horizontalOffset, double contentWidth, TimeSpan duration) =>
        duration > TimeSpan.Zero && double.IsFinite(contentWidth) && contentWidth > 0 && double.IsFinite(viewportX) && double.IsFinite(horizontalOffset)
            ? TimeSpan.FromTicks((long)(Math.Clamp((viewportX + horizontalOffset) / contentWidth, 0, 1) * duration.Ticks)) : null;

    public static double GetDragOffset(double initialOffset, double initialPointer, double pointer, double minimum, double maximum) =>
        double.IsFinite(initialOffset) && double.IsFinite(initialPointer) && double.IsFinite(pointer) && double.IsFinite(minimum) &&
        double.IsFinite(maximum) && minimum <= maximum ? Math.Clamp(initialOffset - (pointer - initialPointer), minimum, maximum) : 0;
}
