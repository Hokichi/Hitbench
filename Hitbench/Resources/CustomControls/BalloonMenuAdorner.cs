using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Hitbench.Resources.CustomControls;

public sealed class BalloonMenuAdorner : Adorner
{
    private readonly FrameworkElement child;
    private readonly VisualCollection children;
    private Size extent;
    private double inset;

    public BalloonMenuAdorner(BalloonMenu owner, FrameworkElement child) : base(owner)
    {
        // The adorner layer owns the logical parent; retain the launcher's resource scopes.
        var scopes = new System.Collections.Generic.Stack<ResourceDictionary>();
        for (DependencyObject? current = owner; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement element) scopes.Push(element.Resources);
        foreach (var scope in scopes) Resources.MergedDictionaries.Add(scope);
        this.child = child;
        children = new VisualCollection(this) { child };
        AddLogicalChild(child);
    }

    internal void UpdatePresentation(double width, double height, double inset)
    {
        var next = new Size(width + inset * 2, height + inset * 2);
        if (extent == next && this.inset == inset) return;
        extent = next;
        this.inset = inset;
        InvalidateMeasure();
    }

    protected override int VisualChildrenCount => children.Count;
    protected override Visual GetVisualChild(int index) => children[index];
    protected override Size MeasureOverride(Size constraint)
    {
        child.Measure(extent);
        return AdornedElement.RenderSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = AdornedElement.RenderSize.Width - extent.Width + inset;
        child.Arrange(new Rect(new Point(x, -inset), extent));
        return finalSize;
    }
}
