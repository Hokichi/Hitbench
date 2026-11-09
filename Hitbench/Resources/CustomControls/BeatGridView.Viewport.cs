using System.Windows;
using System.Windows.Media;
using Hitbench.Resources.Helpers;

namespace Hitbench.Resources.CustomControls;

public sealed partial class BeatGridView
{
    private readonly RetainedTimelineViewport retainedViewport = new();
    private readonly ContainerVisual contentHost = new();
    private readonly TranslateTransform contentTransform = new();
    private readonly RectangleGeometry contentClip = new();
    private void UpdateViewport()
    {
        if (retainedViewport.Update(HorizontalOffset, Math.Min(ActualWidth, ViewportWidth), IsPlaybackMarkerStatic)) contentDirty = true;
        contentTransform.X = retainedViewport.Translation;
        contentClip.Rect = new Rect(RenderSize);
    }
    private Rect RetainedBounds(Rect bounds)
    { if (!bounds.IsEmpty) bounds.Offset(HorizontalOffset - retainedViewport.Origin, 0); return bounds; }
}
