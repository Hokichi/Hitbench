using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Helpers;

namespace osuHitsounder.Views.Pages;

/// <summary>Recycles temporal intervals in the waveform's shared coordinates.</summary>
public sealed class OHsControlPointLanePanel : VirtualizingPanel
{
    private IReadOnlyList<OHsControlPointExtent> extents = [];
    internal Guid? PinnedPointId { get; set; }
    internal Guid? PreviewPointId { get; set; }
    internal double PreviewLeft { get; set; }
    internal double DurationMilliseconds { get; set; }
    internal double ContentWidth { get; set; }
    internal double ViewportWidth { get; set; }
    internal double HorizontalOffset { get; set; }
    internal int RecycledPointCount { get; private set; }
    internal int RealizedPointCount => InternalChildren.Count;
    internal void InvalidateGeometry() => InvalidateMeasure();

    internal IReadOnlyList<OHsControlPointExtent> GetExtents(bool refresh = false)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return [];
        var points = owner.Items.Cast<ControlPointSnapshot>().ToArray();
        if (PreviewPointId is { } id && ContentWidth > 0 && double.IsFinite(PreviewLeft))
            points = points.Select(p => p.Id == id ? p with { Timestamp = (int)Math.Clamp(Math.Round(PreviewLeft / ContentWidth * DurationMilliseconds), 0, int.MaxValue) } : p).ToArray();
        var byId = OHsControlPointLayout.GetExtents(points, DurationMilliseconds, ContentWidth).ToDictionary(e => e.Id);
        extents = points.Where(p => byId.ContainsKey(p.Id)).Select(p => byId[p.Id]).ToArray();
        return extents;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return new();
        GetExtents();
        var wanted = extents.Select((point, index) => (point, index)).Where(item =>
            item.point.Id == PinnedPointId || item.point.Id == PreviewPointId ||
            item.point.Width > 0 && item.point.Left + item.point.Width > HorizontalOffset && item.point.Left < HorizontalOffset + ViewportWidth)
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
            // A pinned zero-duration legacy point gets a temporary edit target, not a temporal interval.
            child.Measure(new(extents[index].Width > 0 ? extents[index].Width : 40, availableSize.Height));
        }
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : InternalChildren.Cast<UIElement>().Select(c => c.DesiredSize.Height).DefaultIfEmpty(0).Max();
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
            var extent = extents[index]; var left = extent.Left - HorizontalOffset;
            var width = extent.Width;
            if (width == 0) { width = Math.Min(40, Math.Max(0, ViewportWidth)); left = Math.Clamp(left, 0, Math.Max(0, ViewportWidth - width)); }
            var child = InternalChildren[childIndex];
            child.Arrange(new(left, 0, width, finalSize.Height));
            if (OHsControlPointLane.FindChild<OHsControlPointZone>(child) is { } zone)
            {
                var visibleLeft = Math.Clamp(-left, 0, width);
                var visibleRight = Math.Clamp(ViewportWidth - left, visibleLeft, width);
                zone.SetVisibleLabelRange(zone.FlowDirection == FlowDirection.RightToLeft ? width - visibleRight : visibleLeft, visibleRight - visibleLeft);
            }
            SetZIndex(child, extent.Id == PinnedPointId || extent.Id == PreviewPointId ? 1 : 0);
        }
        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset) RemoveInternalChildRange(0, InternalChildren.Count);
        base.OnItemsChanged(sender, args); InvalidateMeasure();
    }
}
