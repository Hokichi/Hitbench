using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Hitbench.Core.Entities;
using Track = Hitbench.Core.Entities.Track;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Helpers;
using Hitbench.Services;

namespace Hitbench.Views.Pages;

public partial class OHsWorkstationTrackList
{
    private Point? regionOrigin;
    private Track? regionAnchor;
    private double regionAnchorFraction;
    private Rect regionViewport;
    private TimeSpan regionDuration;
    private double regionWidth, regionZoom;
    private DispatcherTimer? regionScrollTimer;
    private long regionTick;
    private bool updatingRegion;
    private readonly Dictionary<Track, SampleTimeIndex> regionIndices = new(ReferenceEqualityComparer.Instance);
    internal bool IsRegionAutoScrollActive => regionScrollTimer?.IsEnabled == true;
    private BeatGridView? RegionGrid => nodeSourceGrid is { } source && TrackList.IsAncestorOf(source)
        ? source : RealizedTrackGrids(TrackList).FirstOrDefault(grid => TrackList.IsAncestorOf(grid));
    private Rect GetTimelineViewportBounds()
    {
        var presenter = FindVisualChild<ScrollContentPresenter>(TrackList);
        var grid = RegionGrid;
        if (presenter is null) return Rect.Empty;
        var viewport = presenter.TransformToVisual(TrackList).TransformBounds(new Rect(presenter.RenderSize));
        if (grid is null) return new Rect(regionViewport.Left, viewport.Top, regionViewport.Width, viewport.Height);
        var lane = grid.TransformToVisual(TrackList).TransformBounds(new Rect(0, 0, Math.Max(0, Math.Min(grid.ActualWidth, grid.ViewportWidth)), grid.ActualHeight));
        var result = new Rect(lane.Left, viewport.Top, lane.Width, viewport.Height);
        result.Intersect(viewport);
        regionViewport = result;
        regionDuration = grid.Duration; regionWidth = grid.ContentWidth; regionZoom = grid.ZoomFactor;
        return result;
    }
    private double RegionHorizontalOffset => (RegionGrid)?.HorizontalOffset ?? TimelineHorizontalOffset;
    private Point RegionContentPoint(Point point, Rect clip, ScrollViewer scroll) => new(
        Math.Clamp(point.X - clip.Left, 0, clip.Width) + RegionHorizontalOffset,
        Math.Clamp(point.Y - clip.Top, 0, clip.Height) + scroll.VerticalOffset);
    private void CaptureRegionOrigin()
    {
        var clip = GetTimelineViewportBounds();
        if (!clip.IsEmpty && FindVisualChild<ScrollViewer>(TrackList) is { } scroll)
        {
            regionOrigin = RegionContentPoint(TranslatePoint(pointerStart, TrackList), clip, scroll);
            regionAnchor = nodeSourceTrack;
            if (regionAnchor is not null)
            {
                var top = viewModel.Tracks.TakeWhile(track => !ReferenceEquals(track, regionAnchor)).Sum(viewModel.GetTrackExtent);
                regionAnchorFraction = Math.Clamp((regionOrigin.Value.Y - top) / viewModel.GetTrackExtent(regionAnchor), 0, 1);
            }
        }
    }
    private void UpdateRegionSelection(Point pagePoint)
    {
        if (regionAdorner is null || updatingRegion) return;
        if (nodeSourceTrack is not { } source || !viewModel.Tracks.Contains(source) || !viewModel.IsSelectionMode || !viewModel.CanPlaceSamples)
        { CancelNodePointer(); return; }
        var clip = GetTimelineViewportBounds();
        if (clip.IsEmpty || regionOrigin is not { } origin || FindVisualChild<ScrollViewer>(TrackList) is not { } scroll) return;
        if (regionAnchor is not null)
        {
            var top = viewModel.Tracks.TakeWhile(track => !ReferenceEquals(track, regionAnchor)).Sum(viewModel.GetTrackExtent);
            origin.Y = top + regionAnchorFraction * viewModel.GetTrackExtent(regionAnchor);
        }
        updatingRegion = true;
        try
        {
            var pointer = TranslatePoint(pagePoint, TrackList);
            var bounds = new Rect(origin, RegionContentPoint(pointer, clip, scroll));
            var projected = bounds; projected.Offset(clip.Left - RegionHorizontalOffset, clip.Top - scroll.VerticalOffset);
            projected.Intersect(clip);
            regionAdorner.ClipBounds = clip;
            regionAdorner.SelectionBounds = projected;
            var selected = new List<Sample>();
            var top = 0d;
            var reference = RegionGrid;
            foreach (var track in viewModel.Tracks)
            {
                var height = viewModel.GetTrackExtent(track);
                if (bounds.Bottom >= top && bounds.Top <= top + height && viewModel.CanEditTrackGrid(track))
                {
                    if (!regionIndices.TryGetValue(track, out var index)) regionIndices[track] = index = new SampleTimeIndex();
                    index.Update(track.Samples, viewModel.SamplesRevision);
                    var duration = (reference?.Duration ?? regionDuration).TotalMilliseconds;
                    var width = reference?.ContentWidth ?? regionWidth;
                    var half = OHsSampleNodeDrawing.GetBounds(0, reference?.Duration ?? regionDuration, width, 0, 0, height, reference?.ZoomFactor ?? regionZoom).Width / 2;
                    foreach (var sample in width > 0 && duration > 0 ? index.Query((bounds.Left - half) / width * duration, (bounds.Right + half) / width * duration) : [])
                    {
                        var node = OHsSampleNodeDrawing.GetBounds(sample.Timestamp, reference?.Duration ?? regionDuration, reference?.ContentWidth ?? regionWidth, 0, top, height, reference?.ZoomFactor ?? regionZoom);
                        if (!node.IsEmpty && bounds.IntersectsWith(node)) selected.Add(sample);
                    }
                }
                top += height;
            }
            viewModel.SetNodeSelection(selected);
            var outside = !clip.Contains(pointer);
            if (!outside) regionScrollTimer?.Stop();
            else
            {
                if (regionScrollTimer is null)
                {
                    regionScrollTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
                    regionScrollTimer.Tick += (_, _) => { var now = Stopwatch.GetTimestamp(); AdvanceRegionSelection(Stopwatch.GetElapsedTime(regionTick, now)); regionTick = now; };
                }
                if (!regionScrollTimer.IsEnabled) { regionTick = Stopwatch.GetTimestamp(); regionScrollTimer.Start(); }
            }
        }
        finally { updatingRegion = false; }
    }
    internal void AdvanceRegionSelection(TimeSpan elapsed)
    {
        if (regionAdorner is null || !viewModel.CanPlaceSamples || deactivationStarted) { regionScrollTimer?.Stop(); return; }
        var clip = GetTimelineViewportBounds();
        if (clip.IsEmpty || FindVisualChild<ScrollViewer>(TrackList) is not { } scroll) return;
        var point = TranslatePoint(pointerLast, TrackList);
        var x = Math.Clamp(TimelineHorizontalOffset + SelectionAutoScroll.GetDelta(point.X, clip.Left, clip.Right, elapsed), 0, Timeline?.ScrollableWidth ?? 0);
        var y = Math.Clamp(scroll.VerticalOffset + SelectionAutoScroll.GetDelta(point.Y, clip.Top, clip.Bottom, elapsed), 0, scroll.ScrollableHeight);
        if (x == TimelineHorizontalOffset && y == scroll.VerticalOffset) { regionScrollTimer?.Stop(); return; }
        if (x != TimelineHorizontalOffset) Timeline?.ScrollFromUser(x);
        if (y != scroll.VerticalOffset) scroll.ScrollToVerticalOffset(y);
        UpdateRegionSelection(pointerLast);
    }
}
