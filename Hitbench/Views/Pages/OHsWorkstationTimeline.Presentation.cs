namespace Hitbench.Views.Pages;

public partial class OHsWorkstationTimeline
{
    private void PublishPresentationOffset(double offset)
    {
        if (!double.IsFinite(offset)) return;
        var next = Math.Clamp(offset, MinimumHorizontalOffset, ScrollableWidth);
        if (TimelineHorizontalOffset == next) return;
        TimelineHorizontalOffset = next;
        if (scrollBar is not null) scrollBar.Value = next;
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }
}
