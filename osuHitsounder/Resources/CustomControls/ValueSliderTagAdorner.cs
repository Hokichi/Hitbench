using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
namespace osuHitsounder.Resources.CustomControls;
public sealed class ValueSliderTagAdorner : Adorner
{
    private readonly ValueSlider slider;
    private readonly ContentPresenter presenter;
    private readonly VisualCollection children;
    public ValueSliderTagAdorner(ValueSlider slider, DataTemplate template) : base(slider)
    {
        this.slider = slider;
        children = new VisualCollection(this);
        IsHitTestVisible = false; Focusable = false;
        SetBinding(FlowDirectionProperty, new Binding(nameof(FlowDirection)) { Source = slider, Mode = BindingMode.OneWay });
        presenter = new ContentPresenter { Content = slider, ContentTemplate = template, IsHitTestVisible = false, Focusable = false };
        presenter.SetResourceReference(StyleProperty, "BaseContentPresenterStyle");
        children.Add(presenter);
    }
    private (Point Thumb, Point Origin, Size Extent)? position;
    internal void UpdatePosition()
    {
        if (slider.ValueThumb is not { } thumb || Parent is not AdornerLayer layer) return;
        var next = (thumb.TranslatePoint(new Point(thumb.ActualWidth / 2, 0), slider), layer.TranslatePoint(new Point(), slider), layer.RenderSize);
        if (position == next) return;
        position = next;
        InvalidateArrange();
    }
    protected override int VisualChildrenCount => children?.Count ?? 0;
    protected override Visual GetVisualChild(int index) => children[index];
    protected override Size MeasureOverride(Size constraint)
    {
        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return AdornedElement.RenderSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (slider.ValueThumb is not { } thumb) return finalSize;
        var point = thumb.TranslatePoint(new Point(thumb.ActualWidth / 2, 0), slider);
        var size = presenter.DesiredSize;
        var x = point.X - size.Width / 2;
        var y = point.Y - size.Height - 4;
        if (Parent is AdornerLayer layer)
        {
            var origin = layer.TranslatePoint(new Point(), slider);
            x = Math.Clamp(x, origin.X, Math.Max(origin.X, origin.X + layer.ActualWidth - size.Width));
            y = Math.Max(origin.Y, y);
        }
        presenter.Arrange(new Rect(new Point(x, y), size));
        return finalSize;
    }
}
