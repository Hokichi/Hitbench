using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Hitbench.Resources.Helpers;

namespace Hitbench.Views.Pages;

public partial class OHsWorkstationTimeline
{
    internal Func<bool>? CaptureScrubPointer { get; set; }
    internal Action? ReleaseScrubPointer { get; set; }
    internal bool IsScrubbing { get; private set; }
    internal bool IsScrubAutoScrollActive => scrubTimer?.IsEnabled == true;
    private DispatcherTimer? scrubTimer;
    private double scrubPointer;
    private long scrubTick = Stopwatch.GetTimestamp();
    private bool staticScrub;
    private bool scrubDragged;
    private double scrubStartPointer;
    private double scrubStartOffset;

    internal bool BeginScrub(double viewportX)
    {
        EndScrub();
        if (deactivationStarted || !viewModel.CanPlay || !double.IsFinite(viewportX) || TimelineViewportWidth <= 0 ||
            !(CaptureScrubPointer?.Invoke() ?? CaptureMouse())) return false;
        IsScrubbing = true;
        staticScrub = viewModel.IsStaticMarker; scrubDragged = false;
        scrubStartPointer = viewportX; scrubStartOffset = UserHorizontalOffset;
        if (staticScrub) { scrubPointer = viewportX; return true; }
        viewModel.ScrollWithMarker = false;
        UpdateScrub(viewportX); return true;
    }
    internal void UpdateScrub(double viewportX)
    {
        if (!IsScrubbing) return;
        if (!double.IsFinite(viewportX) || deactivationStarted || !viewModel.CanPlay) { EndScrub(); return; }
        scrubPointer = viewportX;
        if (staticScrub)
        {
            scrubDragged |= Math.Abs(viewportX - scrubStartPointer) >= SystemParameters.MinimumHorizontalDragDistance;
            if (scrubDragged) ScrollFromUser(OHsTimelineViewport.GetDragOffset(scrubStartOffset, scrubStartPointer, viewportX, MinimumHorizontalOffset, ScrollableWidth));
            return;
        }
        SeekScrub(TimelineHorizontalOffset);
        if (viewportX < 0 || viewportX > TimelineViewportWidth)
        {
            scrubTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) =>
            {
                var now = Stopwatch.GetTimestamp(); var elapsed = Stopwatch.GetElapsedTime(scrubTick, now); scrubTick = now;
                TickScrub(elapsed);
            }, Dispatcher);
            if (!scrubTimer.IsEnabled) { scrubTick = Stopwatch.GetTimestamp(); scrubTimer.Start(); }
        }
        else scrubTimer?.Stop();
    }
    internal void TickScrub(TimeSpan elapsed)
    {
        if (!IsScrubbing) return;
        if (deactivationStarted || !viewModel.CanPlay) { EndScrub(); return; }
        var next = Math.Clamp(TimelineHorizontalOffset + SelectionAutoScroll.GetDelta(scrubPointer, 0, TimelineViewportWidth, elapsed), MinimumHorizontalOffset, ScrollableWidth);
        ScrollFromUser(next); SeekScrub(next);
    }
    private void SeekScrub(double offset) => SeekViewportPosition(Math.Clamp(scrubPointer, 0, TimelineViewportWidth), offset);
    internal void CompleteScrub(double viewportX)
    {
        if (!IsScrubbing) return;
        UpdateScrub(viewportX);
        if (IsScrubbing && staticScrub && !scrubDragged) SeekViewportPosition(viewportX, scrubStartOffset);
        EndScrub();
    }
    internal void EndScrub()
    {
        scrubTimer?.Stop();
        if (!IsScrubbing) return;
        IsScrubbing = false;
        if (ReleaseScrubPointer is { } release) release(); else if (IsMouseCaptured) ReleaseMouseCapture();
    }
    private double ScrubPointer(MouseEventArgs e) => e.GetPosition(WaveformScrollViewer).X - WaveformScrollViewer.BorderThickness.Left - WaveformScrollViewer.Padding.Left;
    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (!IsScrubbing) return;
        if (e.LeftButton != MouseButtonState.Pressed) EndScrub(); else UpdateScrub(ScrubPointer(e));
        e.Handled = true;
    }
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (!IsScrubbing) return;
        CompleteScrub(ScrubPointer(e)); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); EndScrub(); }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (IsScrubbing && e.Key == Key.Escape) { EndScrub(); e.Handled = true; }
    }
}
