using System.Windows;
using System.Windows.Media;
using Hitbench.Resources.Helpers;

namespace Hitbench.Resources.CustomControls;

public sealed partial class PianoRollView
{
    private readonly RetainedTimelineViewport retainedViewport = new();
    private readonly DrawingVisual chromeVisual = new();
    private readonly DrawingVisual feedbackVisual = new();
    private readonly ContainerVisual contentHost = new();
    private readonly TranslateTransform contentTransform = new();
    private readonly RectangleGeometry contentClip = new();
    private void UpdateViewport()
    {
        if (retainedViewport.Update(HorizontalOffset, TimeViewportWidth, IsPlaybackMarkerStatic)) contentDirty = true;
        contentTransform.X = retainedViewport.Translation;
        contentClip.Rect = new Rect(Math.Max(0, KeyColumnWidth), 0, TimeViewportWidth, Math.Max(0, ActualHeight));
        if (hover is not null && CellAt(pointerLast, out var key, out var ms)) hover = (key, ms);
    }
    private Rect RetainedBounds(Rect bounds)
    { if (!bounds.IsEmpty) bounds.Offset(HorizontalOffset - retainedViewport.Origin, 0); return bounds; }
}
