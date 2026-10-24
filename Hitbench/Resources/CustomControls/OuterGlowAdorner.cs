using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Hitbench.Resources.CustomControls;

public sealed class OuterGlowAdorner : Adorner
{
    private readonly VisualBrush sourceBrush;
    private readonly Border preview;
    private readonly VisualCollection children;
    private ScrollContentPresenter? viewport;
    private DropShadowEffect? glow;
    private (Rect Viewport, Size Source, double Radius, double Clearance)? geometry;
    private double focusClearance;

    public OuterGlowAdorner(FrameworkElement source, ScrollContentPresenter viewport, DropShadowEffect effect) : base(source)
    {
        this.viewport = viewport;
        glow = effect;
        IsHitTestVisible = false;
        Focusable = false;
        Clip = Geometry.Empty;
        SetBinding(FlowDirectionProperty, new Binding(nameof(FlowDirection)) { Source = source, Mode = BindingMode.OneWay });
        sourceBrush = new VisualBrush(source) { ViewboxUnits = BrushMappingMode.Absolute };
        preview = new Border { Background = sourceBrush, Effect = effect, IsHitTestVisible = false, Focusable = false };
        preview.SetResourceReference(StyleProperty, "BaseBorderStyle");
        children = new VisualCollection(this) { preview };
        if (!effect.IsFrozen) effect.Changed += OnEffectChanged;
    }

    public bool UpdateViewport(double focusClearance)
    {
        this.focusClearance = focusClearance;
        var source = AdornedElement;
        if (glow is null || viewport is null || !source.IsVisible || !source.IsDescendantOf(viewport) ||
            source.RenderSize.Width <= 0 || source.RenderSize.Height <= 0)
            return Hide();

        var viewportBounds = viewport.TransformToVisual(source).TransformBounds(new Rect(viewport.RenderSize));
        var sourceBounds = new Rect(source.RenderSize);
        var visible = Rect.Intersect(viewportBounds, sourceBounds);
        if (visible.IsEmpty || visible.Width <= 0 || visible.Height <= 0) return Hide();

        var next = (viewportBounds, source.RenderSize, glow.BlurRadius, focusClearance);
        if (geometry == next) return true;
        var sizeChanged = geometry?.Source != source.RenderSize;
        geometry = next;
        sourceBrush.Viewbox = sourceBounds;
        var envelope = sourceBounds;
        envelope.Inflate(glow.BlurRadius, glow.BlurRadius);
        viewportBounds.Inflate(glow.BlurRadius, glow.BlurRadius);
        envelope.Intersect(viewportBounds);
        sourceBounds.Inflate(focusClearance, focusClearance);
        Clip = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(envelope), new RectangleGeometry(sourceBounds));
        if (sizeChanged) InvalidateMeasure();
        return true;
    }

    private bool Hide()
    {
        geometry = null;
        if (!ReferenceEquals(Clip, Geometry.Empty)) Clip = Geometry.Empty;
        return false;
    }

    private void OnEffectChanged(object? sender, EventArgs e) => UpdateViewport(focusClearance);

    public void Release()
    {
        if (glow is { IsFrozen: false }) glow.Changed -= OnEffectChanged;
        glow = null;
        viewport = null;
        sourceBrush.Visual = null;
        preview.Background = null;
        preview.Effect = null;
        BindingOperations.ClearBinding(this, FlowDirectionProperty);
        Hide();
    }

    protected override int VisualChildrenCount => children?.Count ?? 0;
    protected override Visual GetVisualChild(int index) => children[index];

    protected override Size MeasureOverride(Size constraint)
    {
        preview.Measure(AdornedElement.RenderSize);
        return AdornedElement.RenderSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        preview.Arrange(new Rect(AdornedElement.RenderSize));
        return finalSize;
    }
}
