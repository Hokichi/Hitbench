using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace osuHitsounder.Views.Popups;

/// <summary>Recycles native key containers while sharps overlap adjacent natural keys.</summary>
public sealed class OHsKeysMappingPianoPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty NaturalKeyWidthProperty = RegisterSize(nameof(NaturalKeyWidth), 48d);
    public static readonly DependencyProperty NaturalKeyHeightProperty = RegisterSize(nameof(NaturalKeyHeight), 144d);
    public static readonly DependencyProperty SharpKeyWidthProperty = RegisterSize(nameof(SharpKeyWidth), 28d);
    public static readonly DependencyProperty SharpKeyHeightProperty = RegisterSize(nameof(SharpKeyHeight), 88d);
    public double NaturalKeyWidth { get => (double)GetValue(NaturalKeyWidthProperty); set => SetValue(NaturalKeyWidthProperty, value); }
    public double NaturalKeyHeight { get => (double)GetValue(NaturalKeyHeightProperty); set => SetValue(NaturalKeyHeightProperty, value); }
    public double SharpKeyWidth { get => (double)GetValue(SharpKeyWidthProperty); set => SetValue(SharpKeyWidthProperty, value); }
    public double SharpKeyHeight { get => (double)GetValue(SharpKeyHeightProperty); set => SetValue(SharpKeyHeightProperty, value); }
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth { get; private set; }
    public double ExtentHeight => NaturalKeyHeight;
    public double ViewportWidth { get; private set; }
    public double ViewportHeight { get; private set; }
    public double HorizontalOffset { get; private set; }
    public double VerticalOffset => 0;
    public ScrollViewer? ScrollOwner { get; set; }
    protected override bool HasLogicalOrientation => true;
    protected override Orientation LogicalOrientation => Orientation.Horizontal;

    private static DependencyProperty RegisterSize(string name, double value) => DependencyProperty.Register(name,
        typeof(double), typeof(OHsKeysMappingPianoPanel), new FrameworkPropertyMetadata(value,
            FrameworkPropertyMetadataOptions.AffectsMeasure), v => v is double size && double.IsFinite(size) && size > 0);

    internal Rect GetKeyBounds(int index)
    {
        var pitch = index % 12;
        var sharp = pitch is 1 or 3 or 6 or 8 or 10;
        var natural = pitch switch { 0 => 0, 1 or 2 => 1, 3 or 4 => 2, 5 => 3, 6 or 7 => 4, 8 or 9 => 5, _ => 6 };
        return new((index / 12 * 7 + natural) * NaturalKeyWidth - (sharp ? SharpKeyWidth / 2 : 0),
            0, sharp ? SharpKeyWidth : NaturalKeyWidth, sharp ? SharpKeyHeight : NaturalKeyHeight);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return new();
        ExtentWidth = owner.Items.Count == 0 ? 0 : GetKeyBounds(owner.Items.Count - 1).Right;
        ViewportWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : ExtentWidth;
        ViewportHeight = double.IsFinite(availableSize.Height) ? availableSize.Height : ExtentHeight;
        HorizontalOffset = Math.Clamp(HorizontalOffset, 0, Math.Max(0, ExtentWidth - ViewportWidth));
        ScrollOwner?.InvalidateScrollInfo();
        var wanted = Enumerable.Range(0, owner.Items.Count).Where(index =>
        {
            var bounds = GetKeyBounds(index);
            return bounds.Right >= HorizontalOffset - NaturalKeyWidth && bounds.Left <= HorizontalOffset + ViewportWidth + NaturalKeyWidth;
        }).ToHashSet();
        var generator = (IRecyclingItemContainerGenerator)((IItemContainerGenerator)owner.ItemContainerGenerator).GetItemContainerGeneratorForPanel(this);
        for (var child = InternalChildren.Count - 1; child >= 0; child--)
        {
            var position = new GeneratorPosition(child, 0);
            if (wanted.Contains(generator.IndexFromGeneratorPosition(position)) || InternalChildren[child].IsKeyboardFocusWithin ||
                !GetIsContainerVirtualizable(InternalChildren[child])) continue;
            generator.Recycle(position, 1); RemoveInternalChildRange(child, 1);
        }
        foreach (var index in wanted.Order())
        {
            var position = generator.GeneratorPositionFromIndex(index);
            var childIndex = position.Offset == 0 ? position.Index : position.Index + 1;
            using var generation = generator.StartAt(position, GeneratorDirection.Forward, true);
            var child = (UIElement)generator.GenerateNext(out var isNew);
            if (isNew || InternalChildren.IndexOf(child) < 0)
            {
                InsertInternalChild(Math.Clamp(childIndex, 0, InternalChildren.Count), child);
                generator.PrepareItemContainer(child);
            }
            var bounds = GetKeyBounds(index);
            child.Measure(bounds.Size);
            SetZIndex(child, index % 12 is 1 or 3 or 6 or 8 or 10 ? 1 : 0);
        }
        return new(ViewportWidth, Math.Min(NaturalKeyHeight, ViewportHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return finalSize;
        var generator = (IItemContainerGenerator)owner.ItemContainerGenerator;
        for (var child = 0; child < InternalChildren.Count; child++)
        {
            var index = generator.IndexFromGeneratorPosition(new(child, 0));
            if (index < 0 || index >= owner.Items.Count) continue;
            var bounds = GetKeyBounds(index); bounds.Offset(-HorizontalOffset, 0);
            InternalChildren[child].Arrange(bounds);
        }
        return finalSize;
    }

    protected override void BringIndexIntoView(int index)
    {
        if (ItemsControl.GetItemsOwner(this) is not { } owner || index < 0 || index >= owner.Items.Count) return;
        Reveal(GetKeyBounds(index));
        InvalidateMeasure();
    }
    private void Reveal(Rect bounds)
    {
        if (bounds.Left < HorizontalOffset) SetHorizontalOffset(bounds.Left);
        else if (bounds.Right > HorizontalOffset + ViewportWidth) SetHorizontalOffset(bounds.Right - ViewportWidth);
    }
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (!IsAncestorOf(visual)) return Rect.Empty;
        var bounds = visual.TransformToAncestor(this).TransformBounds(rectangle); bounds.Offset(HorizontalOffset, 0);
        Reveal(bounds); bounds.Offset(-HorizontalOffset, 0);
        return Rect.Intersect(bounds, new Rect(0, 0, ViewportWidth, ViewportHeight));
    }
    public void SetHorizontalOffset(double offset)
    {
        if (double.IsNaN(offset)) return;
        var value = Math.Clamp(offset, 0, Math.Max(0, ExtentWidth - ViewportWidth));
        if (value == HorizontalOffset) return;
        HorizontalOffset = value; InvalidateMeasure(); ScrollOwner?.InvalidateScrollInfo();
    }
    public void SetVerticalOffset(double offset) { }
    public void LineLeft() => SetHorizontalOffset(HorizontalOffset - NaturalKeyWidth);
    public void LineRight() => SetHorizontalOffset(HorizontalOffset + NaturalKeyWidth);
    public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);
    public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);
    public void MouseWheelLeft() => LineLeft();
    public void MouseWheelRight() => LineRight();
    public void MouseWheelUp() => LineLeft();
    public void MouseWheelDown() => LineRight();
    public void LineUp() { }
    public void LineDown() { }
    public void PageUp() { }
    public void PageDown() { }
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset) RemoveInternalChildRange(0, InternalChildren.Count);
        else if (args.ItemUICount > 0)
        {
            var index = args.Action == NotifyCollectionChangedAction.Move ? args.OldPosition.Index : args.Position.Index;
            if (index >= 0 && index < InternalChildren.Count)
                RemoveInternalChildRange(index, Math.Min(args.ItemUICount, InternalChildren.Count - index));
        }
        base.OnItemsChanged(sender, args); InvalidateMeasure();
    }
}
