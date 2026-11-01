using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
namespace Hitbench.Resources.CustomControls;

public sealed class OHsSampleDragAdorner : Adorner
{
    private readonly VisualCollection children;
    private readonly double offset;
    private Rect viewport;
    public string SampleName { get; }
    public ContentPresenter CursorTag { get; }
    public Point? CursorPosition { get; private set; }
    public OHsSampleDragAdorner(FrameworkElement owner, DataTemplate template, string sampleName, double pointerOffset) : base(owner)
    {
        SampleName = sampleName; offset = pointerOffset;
        IsHitTestVisible = false; Focusable = false;
        System.Windows.Input.KeyboardNavigation.SetIsTabStop(this, false);
        CursorTag = new ContentPresenter { Content = this, ContentTemplate = template, IsHitTestVisible = false, Focusable = false };
        CursorTag.SetResourceReference(StyleProperty, "BaseContentPresenterStyle");
        System.Windows.Input.KeyboardNavigation.SetIsTabStop(CursorTag, false);
        children = new VisualCollection(this) { CursorTag };
    }
    public void Update(Point? pointer, Rect viewport)
    {
        if (CursorPosition == pointer && this.viewport == viewport) return;
        CursorPosition = pointer; this.viewport = viewport;
        Clip = new RectangleGeometry(viewport);
        InvalidateMeasure();
    }
    internal static Rect PlaceTag(Point pointer, Rect viewport, Size tagSize, double offset, FlowDirection direction)
    {
        if (viewport.IsEmpty || !double.IsFinite(pointer.X) || !double.IsFinite(pointer.Y) || !double.IsFinite(tagSize.Width) || !double.IsFinite(tagSize.Height) || !viewport.Contains(pointer) || viewport.Width <= 0 || viewport.Height <= 0) return Rect.Empty;
        var width = Math.Min(tagSize.Width, viewport.Width); var height = Math.Min(tagSize.Height, viewport.Height);
        var x = Math.Clamp(direction == FlowDirection.RightToLeft ? pointer.X-width-offset : pointer.X+offset, viewport.Left, viewport.Right-width);
        var y = pointer.Y+offset;
        if (y+height > viewport.Bottom) y = pointer.Y-offset-height;
        return y < viewport.Top ? Rect.Empty : new Rect(x,y,width,height);
    }
    protected override int VisualChildrenCount => children?.Count ?? 0;
    protected override Visual GetVisualChild(int index) => children[index];
    protected override Size MeasureOverride(Size constraint) { CursorTag.Measure(new Size(Math.Max(0,viewport.Width),double.PositiveInfinity)); return AdornedElement.RenderSize; }
    protected override Size ArrangeOverride(Size size)
    {
        var rect = CursorPosition is {} point ? PlaceTag(point,viewport,CursorTag.DesiredSize,offset,FlowDirection) : Rect.Empty;
        CursorTag.Visibility = rect.IsEmpty ? Visibility.Hidden : Visibility.Visible;
        CursorTag.Arrange(rect.IsEmpty ? new Rect() : rect);
        return size;
    }
}
