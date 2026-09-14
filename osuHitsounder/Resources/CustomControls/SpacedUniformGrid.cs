using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace osuHitsounder.Resources.CustomControls;

public class SpacedUniformGrid : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty HorizontalGapProperty = RegisterLayoutProperty(nameof(HorizontalGap), 0d);
    public static readonly DependencyProperty VerticalGapProperty = RegisterLayoutProperty(nameof(VerticalGap), 0d);
    public static readonly DependencyProperty ColumnsProperty = RegisterLayoutProperty(nameof(Columns), 0);
    public static readonly DependencyProperty RowsProperty = RegisterLayoutProperty(nameof(Rows), 0);
    public static readonly DependencyProperty ItemWidthProperty = RegisterLayoutProperty(nameof(ItemWidth), 0d);
    public static readonly DependencyProperty ItemHeightProperty = RegisterLayoutProperty(nameof(ItemHeight), 0d);

    private int computedColumns = 1;
    private int computedRows;
    private double cellWidth;
    private double cellHeight;

    public double HorizontalGap
    {
        get => (double)GetValue(HorizontalGapProperty);
        set => SetValue(HorizontalGapProperty, value);
    }

    public double VerticalGap
    {
        get => (double)GetValue(VerticalGapProperty);
        set => SetValue(VerticalGapProperty, value);
    }

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public int VisibleRowCount => Math.Max(1, (int)Math.Floor((ViewportHeight + VerticalGap) / RowStride));
    public int ItemsPerViewport => checked(VisibleRowCount * Math.Max(1, Columns));
    public double RowStride => ItemHeight + VerticalGap;
    public bool IsPrefetchThresholdReached =>
        ExtentHeight - VerticalOffset - ViewportHeight <= RowStride + 0.01;

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentHeight { get; private set; }
    public double ExtentWidth { get; private set; }
    public double HorizontalOffset { get; private set; }
    public double VerticalOffset { get; private set; }
    public double ViewportHeight { get; private set; }
    public double ViewportWidth { get; private set; }
    public ScrollViewer? ScrollOwner { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var itemCount = owner?.Items.Count ?? InternalChildren.Count;
        ComputeGridDimensions(itemCount);
        if (ItemWidth <= 0 || ItemHeight <= 0)
            return MeasureAutoSized(availableSize, owner, itemCount);

        cellWidth = ItemWidth;
        cellHeight = ItemHeight;
        UpdateScrollInfo(availableSize, GetGridWidth(cellWidth), GetGridHeight(cellHeight));
        if (owner is not null && itemCount > 0)
            RealizeFixedItems(owner, itemCount);
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(cellWidth, cellHeight));

        return new Size(
            double.IsInfinity(availableSize.Width) ? ExtentWidth : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? ExtentHeight : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var generator = owner is null ? null : (IItemContainerGenerator)
            ((IItemContainerGenerator)owner.ItemContainerGenerator).GetItemContainerGeneratorForPanel(this);
        for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            var itemIndex = generator is null
                ? childIndex
                : generator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
            if (itemIndex < 0) continue;
            var logicalColumn = itemIndex % computedColumns;
            var column = FlowDirection == FlowDirection.RightToLeft
                ? computedColumns - 1 - logicalColumn
                : logicalColumn;
            var row = itemIndex / computedColumns;
            var x = column * (cellWidth + HorizontalGap) - HorizontalOffset;
            var y = row * (cellHeight + VerticalGap) - VerticalOffset;
            InternalChildren[childIndex].Arrange(new Rect(x, y, cellWidth, cellHeight));
        }
        return finalSize;
    }

    private void RealizeFixedItems(ItemsControl owner, int itemCount)
    {
        var generator = (IRecyclingItemContainerGenerator)((IItemContainerGenerator)owner.ItemContainerGenerator)
            .GetItemContainerGeneratorForPanel(this);
        var firstRow = Math.Max(0, (int)Math.Floor(VerticalOffset / RowStride));
        var finalVisibleRow = Math.Max(firstRow,
            (int)Math.Ceiling((VerticalOffset + ViewportHeight) / RowStride) - 1);
        var finalRow = Math.Min(computedRows - 1, finalVisibleRow + 1);
        var firstIndex = Math.Min(itemCount - 1, firstRow * computedColumns);
        var finalIndex = Math.Min(itemCount - 1, ((finalRow + 1) * computedColumns) - 1);

        RecycleOutsideRange(generator, firstIndex, finalIndex);

        var start = generator.GeneratorPositionFromIndex(firstIndex);
        var childIndex = start.Offset == 0 ? start.Index : start.Index + 1;
        using var generation = generator.StartAt(start, GeneratorDirection.Forward, true);
        for (var itemIndex = firstIndex; itemIndex <= finalIndex; itemIndex++, childIndex++)
        {
            var child = (UIElement)generator.GenerateNext(out var newlyRealized);
            if (newlyRealized || InternalChildren.IndexOf(child) < 0)
            {
                if (childIndex >= InternalChildren.Count) AddInternalChild(child);
                else InsertInternalChild(childIndex, child);
                generator.PrepareItemContainer(child);
            }
            child.Measure(new Size(cellWidth, cellHeight));
        }
    }

    private void RecycleOutsideRange(IRecyclingItemContainerGenerator generator, int firstIndex, int finalIndex)
    {
        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var position = new GeneratorPosition(childIndex, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(position);
            if (itemIndex >= firstIndex && itemIndex <= finalIndex) continue;
            generator.Recycle(position, 1);
            RemoveInternalChildRange(childIndex, 1);
        }
    }

    private Size MeasureAutoSized(Size availableSize, ItemsControl? owner, int itemCount)
    {
        if (owner is not null && itemCount > 0)
            RealizeAllItems(owner, itemCount);
        if (InternalChildren.Count == 0)
        {
            UpdateScrollInfo(availableSize, 0, 0);
            return new Size();
        }

        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        cellWidth = InternalChildren.Cast<UIElement>().Max(child => child.DesiredSize.Width);
        cellHeight = InternalChildren.Cast<UIElement>().Max(child => child.DesiredSize.Height);
        if (!double.IsInfinity(availableSize.Width))
            cellWidth = Math.Max(0, (availableSize.Width - HorizontalGap * (computedColumns - 1)) / computedColumns);
        if (!double.IsInfinity(availableSize.Height))
            cellHeight = Math.Max(0, (availableSize.Height - VerticalGap * (computedRows - 1)) / Math.Max(1, computedRows));
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(cellWidth, cellHeight));

        var width = GetGridWidth(cellWidth);
        var height = GetGridHeight(cellHeight);
        UpdateScrollInfo(availableSize, width, height);
        return new Size(width, height);
    }

    private void RealizeAllItems(ItemsControl owner, int itemCount)
    {
        var generator = (IItemContainerGenerator)((IItemContainerGenerator)owner.ItemContainerGenerator)
            .GetItemContainerGeneratorForPanel(this);
        var start = generator.GeneratorPositionFromIndex(0);
        var childIndex = 0;
        using var generation = generator.StartAt(start, GeneratorDirection.Forward, true);
        for (var itemIndex = 0; itemIndex < itemCount; itemIndex++, childIndex++)
        {
            var child = (UIElement)generator.GenerateNext(out var newlyRealized);
            if (newlyRealized || InternalChildren.IndexOf(child) < 0)
            {
                if (childIndex >= InternalChildren.Count) AddInternalChild(child);
                else InsertInternalChild(childIndex, child);
                generator.PrepareItemContainer(child);
            }
        }
    }

    private void ComputeGridDimensions(int itemCount)
    {
        if (Columns > 0 && Rows > 0)
        {
            computedColumns = Columns;
            computedRows = Rows;
        }
        else if (Columns > 0)
        {
            computedColumns = Columns;
            computedRows = (int)Math.Ceiling((double)itemCount / Columns);
        }
        else if (Rows > 0)
        {
            computedRows = Rows;
            computedColumns = Math.Max(1, (int)Math.Ceiling((double)itemCount / Rows));
        }
        else
        {
            computedColumns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(itemCount)));
            computedRows = (int)Math.Ceiling((double)itemCount / computedColumns);
        }
    }

    private double GetGridWidth(double width) =>
        computedColumns * width + Math.Max(0, computedColumns - 1) * HorizontalGap;

    private double GetGridHeight(double height) => computedRows == 0
        ? 0
        : computedRows * height + (computedRows - 1) * VerticalGap;

    private void UpdateScrollInfo(Size availableSize, double extentWidth, double extentHeight)
    {
        var viewportWidth = double.IsInfinity(availableSize.Width) ? extentWidth : Math.Max(0, availableSize.Width);
        var viewportHeight = double.IsInfinity(availableSize.Height) ? extentHeight : Math.Max(0, availableSize.Height);
        var changed = !AreClose(ExtentWidth, extentWidth) || !AreClose(ExtentHeight, extentHeight) ||
            !AreClose(ViewportWidth, viewportWidth) || !AreClose(ViewportHeight, viewportHeight);
        ExtentWidth = extentWidth;
        ExtentHeight = extentHeight;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        HorizontalOffset = Clamp(HorizontalOffset, ExtentWidth, ViewportWidth);
        VerticalOffset = Clamp(VerticalOffset, ExtentHeight, ViewportHeight);
        if (changed) ScrollOwner?.InvalidateScrollInfo();
    }

    public void LineUp() => SetVerticalOffset(VerticalOffset - RowStride);
    public void LineDown() => SetVerticalOffset(VerticalOffset + RowStride);
    public void LineLeft() => SetHorizontalOffset(HorizontalOffset - ItemWidth - HorizontalGap);
    public void LineRight() => SetHorizontalOffset(HorizontalOffset + ItemWidth + HorizontalGap);
    public void MouseWheelUp() => LineUp();
    public void MouseWheelDown() => LineDown();
    public void MouseWheelLeft() => LineLeft();
    public void MouseWheelRight() => LineRight();
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);
    public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);

    public void SetHorizontalOffset(double offset)
    {
        var value = CanHorizontallyScroll ? Clamp(offset, ExtentWidth, ViewportWidth) : 0;
        if (AreClose(value, HorizontalOffset)) return;
        HorizontalOffset = value;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public void SetVerticalOffset(double offset)
    {
        var value = CanVerticallyScroll ? Clamp(offset, ExtentHeight, ViewportHeight) : 0;
        if (AreClose(value, VerticalOffset)) return;
        VerticalOffset = value;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is not UIElement element || !element.IsDescendantOf(this)) return Rect.Empty;
        var bounds = visual.TransformToAncestor(this).TransformBounds(rectangle);
        bounds.Offset(HorizontalOffset, VerticalOffset);

        if (bounds.Left < HorizontalOffset) SetHorizontalOffset(bounds.Left);
        else if (bounds.Right > HorizontalOffset + ViewportWidth)
            SetHorizontalOffset(bounds.Right - ViewportWidth);
        if (bounds.Top < VerticalOffset) SetVerticalOffset(bounds.Top);
        else if (bounds.Bottom > VerticalOffset + ViewportHeight)
            SetVerticalOffset(bounds.Bottom - ViewportHeight);

        var viewport = new Rect(HorizontalOffset, VerticalOffset, ViewportWidth, ViewportHeight);
        return Rect.Intersect(bounds, viewport);
    }

    private static double Clamp(double value, double extent, double viewport)
    {
        if (double.IsNaN(value)) return 0;
        return Math.Max(0, Math.Min(value, Math.Max(0, extent - viewport)));
    }

    private static bool AreClose(double first, double second) => Math.Abs(first - second) < 0.01;

    private static DependencyProperty RegisterLayoutProperty<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(SpacedUniformGrid),
            new FrameworkPropertyMetadata(defaultValue,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));
}
