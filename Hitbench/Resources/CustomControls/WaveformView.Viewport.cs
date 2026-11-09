using System.Windows;
using System.Windows.Media;
using Hitbench.Resources.Helpers;

namespace Hitbench.Resources.CustomControls;

public sealed partial class WaveformView
{
    public static readonly DependencyProperty LayoutHorizontalOffsetProperty = DependencyProperty.Register(
        nameof(LayoutHorizontalOffset), typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double LayoutHorizontalOffset { get => (double)GetValue(LayoutHorizontalOffsetProperty); set => SetValue(LayoutHorizontalOffsetProperty, value); }
    private readonly RetainedTimelineViewport retainedViewport = new();
    private readonly ContainerVisual contentHost = new();
    private readonly TranslateTransform contentTransform = new();
    private readonly RectangleGeometry contentClip = new();
    internal int ContentBuildCount { get; private set; }

    private void UpdateViewport()
    {
        var viewport = IsPositiveFinite(ViewportWidth) ? Math.Min(ViewportWidth, ActualWidth) : ActualWidth;
        var offset = double.IsFinite(HorizontalOffset) ? HorizontalOffset : 0;
        if (!IsPlaybackMarkerStatic) offset = Math.Clamp(offset, 0, Math.Max(0, ActualWidth - viewport));
        if (retainedViewport.Update(offset, viewport, IsPlaybackMarkerStatic && BarCount == 0)) contentDirty = true;
        contentTransform.X = IsPlaybackMarkerStatic ? LayoutHorizontalOffset - offset : 0;
        contentClip.Rect = new Rect(IsPlaybackMarkerStatic ? LayoutHorizontalOffset : offset, 0, Math.Max(0, viewport), Math.Max(0, ActualHeight));
    }
}
