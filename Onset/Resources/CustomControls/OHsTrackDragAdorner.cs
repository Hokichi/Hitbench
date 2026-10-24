using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace Onset.Resources.CustomControls;

public sealed class OHsTrackDragAdorner : Adorner
{
    private static readonly DependencyPropertyKey TrackNamePropertyKey = DependencyProperty.RegisterReadOnly(nameof(TrackName), typeof(string), typeof(OHsTrackDragAdorner), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty TrackNameProperty = TrackNamePropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PreviewSourcePropertyKey = DependencyProperty.RegisterReadOnly(nameof(PreviewSource), typeof(ImageSource), typeof(OHsTrackDragAdorner), new PropertyMetadata(null));
    public static readonly DependencyProperty PreviewSourceProperty = PreviewSourcePropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PreviewExtentPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PreviewExtent), typeof(double), typeof(OHsTrackDragAdorner), new PropertyMetadata(0d));
    public static readonly DependencyProperty PreviewExtentProperty = PreviewExtentPropertyKey.DependencyProperty;
    public string TrackName => (string)GetValue(TrackNameProperty);
    public ImageSource? PreviewSource => (ImageSource?)GetValue(PreviewSourceProperty);
    public double PreviewExtent => (double)GetValue(PreviewExtentProperty);
    public ContentPresenter CursorTag { get; }
    public ContentPresenter Phantom { get; }
    private readonly VisualCollection children;
    private Point pointer;
    private Rect viewport;
    private Rect? gap;

    public OHsTrackDragAdorner(UIElement adornedElement, DataTemplate tagTemplate, DataTemplate phantomTemplate) : base(adornedElement)
    {
        children = new VisualCollection(this);
        IsHitTestVisible = false; Focusable = false;
        SetBinding(FlowDirectionProperty, new Binding(nameof(FlowDirection)) { Source = adornedElement, Mode = BindingMode.OneWay });
        CursorTag = new ContentPresenter { Content = this, ContentTemplate = tagTemplate, IsHitTestVisible = false, Focusable = false };
        Phantom = new ContentPresenter { Content = this, ContentTemplate = phantomTemplate, IsHitTestVisible = false, Focusable = false };
        CursorTag.SetResourceReference(StyleProperty, "BaseContentPresenterStyle");
        Phantom.SetResourceReference(StyleProperty, "BaseContentPresenterStyle");
        children.Add(Phantom); children.Add(CursorTag);
    }

    public void Update(Point pointer, Rect viewport, Rect? insertionGap, string name, ImageSource preview, double extent)
    {
        var contentChanged = TrackName != name || !ReferenceEquals(PreviewSource, preview) || PreviewExtent != extent;
        SetValue(TrackNamePropertyKey, name); SetValue(PreviewSourcePropertyKey, preview); SetValue(PreviewExtentPropertyKey, extent);
        if (!contentChanged && this.pointer == pointer && this.viewport == viewport && gap == insertionGap) return;
        this.pointer = pointer; this.viewport = viewport; gap = insertionGap;
        Clip = new RectangleGeometry(viewport);
        Phantom.Visibility = gap is null ? Visibility.Collapsed : Visibility.Visible;
        InvalidateMeasure();
    }
    protected override int VisualChildrenCount => children?.Count ?? 0;
    protected override Visual GetVisualChild(int index) => children[index];
    protected override Size MeasureOverride(Size constraint)
    {
        CursorTag.Measure(new Size(Math.Max(0, viewport.Width), double.PositiveInfinity));
        Phantom.Measure(new Size(Math.Max(0, viewport.Width), Math.Max(0, PreviewExtent)));
        return AdornedElement.RenderSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Math.Min(viewport.Width, CursorTag.DesiredSize.Width);
        var height = Math.Min(viewport.Height, CursorTag.DesiredSize.Height);
        var x = FlowDirection == FlowDirection.RightToLeft ? pointer.X - width - 12 : pointer.X + 12;
        var y = pointer.Y + 12;
        CursorTag.Arrange(new Rect(Math.Clamp(x, viewport.Left, viewport.Right - width), Math.Clamp(y, viewport.Top, viewport.Bottom - height), width, height));
        Phantom.Arrange(gap ?? new Rect());
        return finalSize;
    }
}
