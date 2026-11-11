using System.Windows;
using Hitbench.Resources.Helpers;

namespace Hitbench.Views.Pages;

public partial class OHsWorkstationTimeline
{
    public static readonly DependencyProperty DisplayStartMillisecondsProperty = DependencyProperty.Register(nameof(DisplayStartMilliseconds), typeof(double), typeof(OHsWorkstationTimeline), new PropertyMetadata(0d, OnDisplayExtentChanged));
    public static readonly DependencyProperty DisplayEndMillisecondsProperty = DependencyProperty.Register(nameof(DisplayEndMilliseconds), typeof(double), typeof(OHsWorkstationTimeline), new PropertyMetadata(double.NaN, OnDisplayExtentChanged));
    public double DisplayStartMilliseconds { get => (double)GetValue(DisplayStartMillisecondsProperty); set => SetValue(DisplayStartMillisecondsProperty, value); }
    public double DisplayEndMilliseconds { get => (double)GetValue(DisplayEndMillisecondsProperty); set => SetValue(DisplayEndMillisecondsProperty, value); }
    private static readonly DependencyPropertyKey TimelineScrollOriginPaddingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(TimelineScrollOriginPadding), typeof(double), typeof(OHsWorkstationTimeline), new PropertyMetadata(0d));
    public static readonly DependencyProperty TimelineScrollOriginPaddingProperty = TimelineScrollOriginPaddingPropertyKey.DependencyProperty;
    public double TimelineScrollOriginPadding => (double)GetValue(TimelineScrollOriginPaddingProperty);
    private bool inspectingPreservedNotes;
    private TimeSpan inspectionPosition;
    private static void OnDisplayExtentChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var timeline = (OHsWorkstationTimeline)owner;
        if (timeline.WaveformExtent is null) return;
        timeline.inspectingPreservedNotes = false;
        var offset = timeline.TimelineHorizontalOffset;
        timeline.RefreshTimelineGeometry();
        timeline.ScrollToHorizontalOffset(offset);
    }
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
        inspectingPreservedNotes = false;
        WaveformScrollViewer.ScrollToHorizontalOffset(TimelineHorizontalOffset + TimelineScrollOriginPadding);
        if (viewModel.IsStaticMarker) CenterPlaybackPosition();
        else RevealPlaybackPosition();
    }

    private void RefreshTimelineGeometry()
    {
        var padding = viewModel?.IsStaticMarker == true ? Math.Max(0, TimelineViewportWidth) / 2 : 0;
        SetValue(TimelineEdgePaddingPropertyKey, padding);
        var duration = viewModel?.Waveform?.Duration.TotalMilliseconds ?? 0;
        var minimumX = duration > 0 && double.IsFinite(DisplayStartMilliseconds) ? Math.Min(0, DisplayStartMilliseconds) / duration * TimelineContentWidth : 0;
        var maximumX = duration > 0 && double.IsFinite(DisplayEndMilliseconds) ? Math.Max(duration, DisplayEndMilliseconds) / duration * TimelineContentWidth : TimelineContentWidth;
        var left = padding - minimumX;
        SetValue(TimelineScrollOriginPaddingPropertyKey, left);
        WaveformExtent.Padding = new Thickness(left, 0, padding + maximumX - TimelineContentWidth, 0);
        var bounds = OHsTimelineViewport.GetHorizontalBounds(TimelineContentWidth, TimelineViewportWidth, padding, minimumX, maximumX);
        SetValue(MinimumHorizontalOffsetPropertyKey, bounds.Minimum);
        // Historical public name: this is the maximum logical offset, not the padded extent.
        SetValue(ScrollableWidthPropertyKey, bounds.Maximum);
    }

    private void CenterPlaybackPosition()
    {
        if (deactivationStarted || navigating || viewModel.Waveform is not { } waveform) return;
        if (inspectingPreservedNotes && !viewModel.IsPlaying && viewModel.Position == inspectionPosition) return;
        inspectingPreservedNotes = false;
        ScrollToHorizontalOffset(OHsTimelineViewport.GetCenteredOffset(viewModel.DisplayPosition, waveform.Duration,
            TimelineContentWidth, TimelineViewportWidth));
    }
}
