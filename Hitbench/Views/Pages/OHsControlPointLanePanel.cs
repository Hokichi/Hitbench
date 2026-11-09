using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Helpers;

namespace Hitbench.Views.Pages;

/// <summary>Recycles temporal intervals in the waveform's shared coordinates.</summary>
public sealed class OHsControlPointLanePanel : VirtualizingPanel
{
    private IReadOnlyList<OHsControlPointExtent> extents = [];
    private readonly RetainedTimelineViewport retainedViewport = new();
    private readonly TranslateTransform translation = new();
    private bool geometryValid;
    private (double, double, Guid?, double, Guid?, int?) geometryKey;
    internal bool IsRetained { get; set; }
    internal int GeometryBuildCount { get; private set; }
    internal int MeasureCount { get; private set; }
    public OHsControlPointLanePanel() => RenderTransform = translation;
    internal Guid? PreviewVolumePointId { get; set; }
    internal int? PreviewVolume { get; set; }
    private IReadOnlyDictionary<Guid, OHsControlPointCurveProjection> curves = new Dictionary<Guid, OHsControlPointCurveProjection>();
    internal Guid? PinnedPointId { get; set; }
    internal Guid? PreviewPointId { get; set; }
    internal double PreviewLeft { get; set; }
    internal double DurationMilliseconds { get; set; }
    internal double ContentWidth { get; set; }
    internal double ViewportWidth { get; set; }
    internal double HorizontalOffset { get; set; }
    internal int RecycledPointCount { get; private set; }
    internal int RealizedPointCount => InternalChildren.Count;
    internal void InvalidateGeometry() { geometryValid = false; retainedViewport.Invalidate(); InvalidateMeasure(); }

    internal void UpdateHorizontalOffset(double offset)
    {
        HorizontalOffset = offset;
        if (retainedViewport.Update(offset, ViewportWidth, IsRetained)) InvalidateMeasure();
        translation.X = retainedViewport.Translation;
        UpdateLabels(false);
    }

    internal IReadOnlyList<OHsControlPointExtent> GetExtents()
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return [];
        var key = (DurationMilliseconds, ContentWidth, PreviewPointId, PreviewLeft, PreviewVolumePointId, PreviewVolume);
        if (geometryValid && geometryKey == key) return extents;
        geometryKey = key; geometryValid = true; GeometryBuildCount++;
        var points = owner.Items.Cast<ControlPointSnapshot>().ToArray();
        if (PreviewPointId is { } id && ContentWidth > 0 && double.IsFinite(PreviewLeft))
            points = points.Select(p => p.Id == id ? p with { Timestamp = (int)Math.Clamp(Math.Round(PreviewLeft / ContentWidth * DurationMilliseconds), 0, int.MaxValue) } : p).ToArray();
        if (PreviewVolumePointId is { } volumeId && PreviewVolume is { } volume)
            points = points.Select(p => p.Id == volumeId ? p with { Volume = volume } : p).ToArray();
        curves = OHsControlPointLayout.GetCurveProjections(points, DurationMilliseconds).ToDictionary(p => p.PointId);
        var byId = OHsControlPointLayout.GetExtents(points, DurationMilliseconds, ContentWidth).ToDictionary(e => e.Id);
        extents = points.Where(p => byId.ContainsKey(p.Id)).Select(p => byId[p.Id]).ToArray();
        return extents;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        MeasureCount++;
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return new();
        GetExtents();
        retainedViewport.Update(HorizontalOffset, ViewportWidth, IsRetained);
        var wanted = extents.Select((point, index) => (point, index)).Where(item =>
            item.point.Id == PinnedPointId || item.point.Id == PreviewPointId ||
            item.point.Width > 0 && item.point.Left + item.point.Width > retainedViewport.Origin + retainedViewport.Start && item.point.Left < retainedViewport.Origin + retainedViewport.Start + retainedViewport.Width)
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
            var extent = extents[index]; var left = extent.Left - retainedViewport.Origin;
            var width = extent.Width;
            if (width == 0) { width = Math.Min(40, Math.Max(0, ViewportWidth)); left = Math.Clamp(left, 0, Math.Max(0, ViewportWidth - width)); }
            var child = InternalChildren[childIndex];
            child.Arrange(new(left, 0, width, finalSize.Height));
            if (OHsControlPointLane.FindChild<OHsControlPointZone>(child) is { } zone)
            {
                zone.Curve = curves.GetValueOrDefault(extent.Id);
            }
            SetZIndex(child, extent.Id == PinnedPointId || extent.Id == PreviewPointId ? 1 : 0);
        }
        translation.X = retainedViewport.Translation;
        UpdateLabels(true);
        return finalSize;
    }

    private void UpdateLabels(bool updateWidth)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return;
        var generator = (IItemContainerGenerator)((IItemContainerGenerator)owner.ItemContainerGenerator).GetItemContainerGeneratorForPanel(this);
        for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            var index = generator.IndexFromGeneratorPosition(new(childIndex, 0));
            if (index < 0 || index >= extents.Count || OHsControlPointLane.FindChild<OHsControlPointZone>(InternalChildren[childIndex]) is not { } zone) continue;
            var extent = extents[index]; var left = extent.Left - HorizontalOffset; var width = extent.Width;
            var child = InternalChildren[childIndex];
            if (width == 0)
            {
                width = Math.Min(40, Math.Max(0, ViewportWidth));
                left = Math.Clamp(left, 0, Math.Max(0, ViewportWidth - width));
                var arrangedLeft = Math.Clamp(extent.Left - retainedViewport.Origin, 0, Math.Max(0, ViewportWidth - width));
                if (child.RenderTransform is not TranslateTransform adjustment) child.RenderTransform = adjustment = new TranslateTransform();
                adjustment.X = left - arrangedLeft - translation.X;
            }
            else if (child.RenderTransform is TranslateTransform adjustment) adjustment.X = 0;
            var visibleLeft = Math.Clamp(-left, 0, width);
            var visibleRight = Math.Clamp(ViewportWidth - left, visibleLeft, width);
            zone.SetVisibleLabelRange(zone.FlowDirection == FlowDirection.RightToLeft ? width - visibleRight : visibleLeft,
                visibleRight - visibleLeft, updateWidth, IsRetained ? Math.Min(width, ViewportWidth) : null);
        }
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset) RemoveInternalChildRange(0, InternalChildren.Count);
        base.OnItemsChanged(sender, args); InvalidateGeometry();
    }
}
