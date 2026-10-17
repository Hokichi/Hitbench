using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Helpers;

namespace osuHitsounder.Views.Pages;

/// <summary>Virtualized timestamp layout; the waveform remains the scrolling owner.</summary>
public sealed class OHsControlPointLanePanel : VirtualizingPanel
{
    private readonly Dictionary<Guid, (ControlPointSnapshot Point, Size Size)> measurements = [];
    private IReadOnlyList<OHsControlPointExtent> extents = [];
    internal Func<ControlPointSnapshot, Size>? MeasurePoint { get; set; }
    internal Guid? PinnedPointId { get; set; }
    internal Guid? PreviewPointId { get; set; }
    internal double PreviewLeft { get; set; }
    internal double DurationMilliseconds { get; set; }
    internal double ContentWidth { get; set; }
    internal double ViewportWidth { get; set; }
    internal double HorizontalOffset { get; set; }
    internal int RecycledPointCount { get; private set; }
    internal int RealizedPointCount => InternalChildren.Count;
    internal void InvalidateGeometry() { measurements.Clear(); InvalidateMeasure(); }

    internal IReadOnlyList<OHsControlPointExtent> GetExtents(bool refresh = false)
    {
        if (refresh) measurements.Clear();
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null || MeasurePoint is null) return [];
        var current = new List<OHsControlPointExtent>(owner.Items.Count);
        foreach (ControlPointSnapshot point in owner.Items)
        {
            if (!measurements.TryGetValue(point.Id, out var cached) || cached.Point.Volume != point.Volume || cached.Point.Sampleset != point.Sampleset)
                measurements[point.Id] = cached = (point, MeasurePoint(point));
            current.Add(new(point.Id, point.Timestamp, OHsControlPointLayout.GetLeft(point.Timestamp, DurationMilliseconds, ContentWidth), cached.Size.Width));
        }
        extents = current;
        return extents;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null || MeasurePoint is null) return new();
        GetExtents();
        var wanted = extents.Select((point, index) => (point, index)).Where(item =>
            item.point.Id == PinnedPointId || item.point.Id == PreviewPointId ||
            item.point.Left + item.point.Width >= HorizontalOffset && item.point.Left <= HorizontalOffset + ViewportWidth)
            .Select(item => item.index).ToHashSet();
        var generator = (IRecyclingItemContainerGenerator)((IItemContainerGenerator)owner.ItemContainerGenerator).GetItemContainerGeneratorForPanel(this);
        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var position = new GeneratorPosition(childIndex, 0);
            if (wanted.Contains(generator.IndexFromGeneratorPosition(position))) continue;
            generator.Recycle(position, 1); RemoveInternalChildRange(childIndex, 1); RecycledPointCount++;
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
            child.Measure(measurements[extents[index].Id].Size);
        }
        var height = measurements.Count == 0 ? 0 : measurements.Values.Max(item => item.Size.Height);
        return new(double.IsFinite(availableSize.Width) ? availableSize.Width : Math.Max(0, ViewportWidth), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return finalSize;
        var generator = (IItemContainerGenerator)((IItemContainerGenerator)owner.ItemContainerGenerator).GetItemContainerGeneratorForPanel(this);
        for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            var index = generator.IndexFromGeneratorPosition(new(childIndex, 0));
            if (index < 0 || index >= extents.Count) continue;
            var extent = extents[index]; var left = extent.Id == PreviewPointId ? PreviewLeft : extent.Left;
            if (!double.IsFinite(left)) continue;
            var child = InternalChildren[childIndex];
            child.Arrange(new(left - HorizontalOffset, 0, extent.Width, measurements[extent.Id].Size.Height));
            SetZIndex(child, extent.Id == PinnedPointId || extent.Id == PreviewPointId ? 1 : 0);
        }
        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset) { RemoveInternalChildRange(0, InternalChildren.Count); measurements.Clear(); }
        base.OnItemsChanged(sender, args); InvalidateMeasure();
    }
}
