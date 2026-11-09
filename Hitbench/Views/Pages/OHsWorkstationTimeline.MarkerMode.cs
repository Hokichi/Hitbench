using System.Windows;
using Hitbench.Resources.Helpers;

namespace Hitbench.Views.Pages;

public partial class OHsWorkstationTimeline
{
    private static readonly DependencyPropertyKey TimelineEdgePaddingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(TimelineEdgePadding), typeof(double), typeof(OHsWorkstationTimeline), new PropertyMetadata(0d));
    public static readonly DependencyProperty TimelineEdgePaddingProperty = TimelineEdgePaddingPropertyKey.DependencyProperty;
    public double TimelineEdgePadding => (double)GetValue(TimelineEdgePaddingProperty);
    private static readonly DependencyPropertyKey MinimumHorizontalOffsetPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(MinimumHorizontalOffset), typeof(double), typeof(OHsWorkstationTimeline), new PropertyMetadata(0d));
    public static readonly DependencyProperty MinimumHorizontalOffsetProperty = MinimumHorizontalOffsetPropertyKey.DependencyProperty;
    public double MinimumHorizontalOffset => (double)GetValue(MinimumHorizontalOffsetProperty);

    public void ApplyMarkerMode()
    {
        EndScrub(); hasPendingZoomAnchor = false;
        RefreshTimelineGeometry();
        WaveformScrollViewer.ScrollToHorizontalOffset(TimelineHorizontalOffset + TimelineEdgePadding);
        if (viewModel.IsStaticMarker) CenterPlaybackPosition();
        else RevealPlaybackPosition();
    }

    private void RefreshTimelineGeometry()
    {
        var padding = viewModel?.IsStaticMarker == true ? Math.Max(0, TimelineViewportWidth) / 2 : 0;
        SetValue(TimelineEdgePaddingPropertyKey, padding);
        WaveformExtent.Padding = new Thickness(padding, 0, padding, 0);
        var bounds = OHsTimelineViewport.GetHorizontalBounds(TimelineContentWidth, TimelineViewportWidth, padding);
        SetValue(MinimumHorizontalOffsetPropertyKey, bounds.Minimum);
        // Historical public name: this is the maximum logical offset, not the padded extent.
        SetValue(ScrollableWidthPropertyKey, bounds.Maximum);
    }

    private void CenterPlaybackPosition()
    {
        if (deactivationStarted || navigating || viewModel.Waveform is not { } waveform) return;
        ScrollToHorizontalOffset(OHsTimelineViewport.GetCenteredOffset(viewModel.DisplayPosition, waveform.Duration,
            TimelineContentWidth, TimelineViewportWidth));
    }
}
