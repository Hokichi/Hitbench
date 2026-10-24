using System;
using System.Windows;
using System.Windows.Controls;

namespace Onset.Resources.CustomControls;

public sealed class SegmentedTogglePanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var visibleChildrenCount = CountVisibleChildren();
        if (visibleChildrenCount == 0)
            return default;

        var spacing = GetOptionSpacing();
        var totalSpacing = spacing * Math.Max(0, visibleChildrenCount - 1);

        if (double.IsInfinity(availableSize.Width))
            return MeasureAutoWidth(availableSize, visibleChildrenCount, totalSpacing);

        var itemWidth = Math.Max(0, (availableSize.Width - totalSpacing) / visibleChildrenCount);
        var childConstraint = new Size(itemWidth, availableSize.Height);
        var desiredHeight = 0.0;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            child.Measure(childConstraint);
            desiredHeight = Math.Max(desiredHeight, child.DesiredSize.Height);
        }

        return new Size(availableSize.Width, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visibleChildrenCount = CountVisibleChildren();
        if (visibleChildrenCount == 0)
            return finalSize;

        var spacing = GetOptionSpacing();
        var totalSpacing = spacing * Math.Max(0, visibleChildrenCount - 1);
        var itemWidth = Math.Max(0, (finalSize.Width - totalSpacing) / visibleChildrenCount);
        var x = 0.0;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            child.Arrange(new Rect(x, 0, itemWidth, finalSize.Height));
            x += itemWidth + spacing;
        }

        return finalSize;
    }

    private Size MeasureAutoWidth(Size availableSize, int visibleChildrenCount, double totalSpacing)
    {
        var maxDesiredWidth = 0.0;
        var maxDesiredHeight = 0.0;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            maxDesiredWidth = Math.Max(maxDesiredWidth, child.DesiredSize.Width);
            maxDesiredHeight = Math.Max(maxDesiredHeight, child.DesiredSize.Height);
        }

        return new Size((maxDesiredWidth * visibleChildrenCount) + totalSpacing, maxDesiredHeight);
    }

    private int CountVisibleChildren()
    {
        var count = 0;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility != Visibility.Collapsed)
                count++;
        }

        return count;
    }

    private double GetOptionSpacing()
    {
        return ItemsControl.GetItemsOwner(this) is SegmentedToggleGroup group
            ? group.OptionSpacing
            : 0;
    }
}


