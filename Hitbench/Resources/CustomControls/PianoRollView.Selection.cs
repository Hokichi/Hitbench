using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Hitbench.Resources.Helpers;

namespace Hitbench.Resources.CustomControls;

public sealed partial class PianoRollView
{
    private Point selectionOrigin;
    private DispatcherTimer? selectionTimer;
    private long selectionTick;
    private bool IsRectangleGesture => pointerInteraction && dragging && pressedNote is null && !IsAdditionMode && !pointerModifiers.HasFlag(ModifierKeys.Control);
    internal bool IsRegionAutoScrollActive => selectionTimer?.IsEnabled == true;
    private Point SelectionContentPoint(Point point) => new(Math.Clamp(point.X - KeyColumnWidth, 0, TimeViewportWidth) + HorizontalOffset,
        Math.Clamp(point.Y, 0, Math.Max(0, ActualHeight)) + VerticalOffset);
    private Rect SelectionContentBounds => new(selectionOrigin, SelectionContentPoint(pointerLast));
    private Rect SelectionViewportBounds
    {
        get { var bounds = SelectionContentBounds; bounds.Offset(KeyColumnWidth - HorizontalOffset, -VerticalOffset); return bounds; }
    }
    private void UpdateSelectionScroll()
    {
        if (!IsRectangleGesture || !CanEdit || !IsEnabled || !double.IsFinite(pointerLast.X) || !double.IsFinite(pointerLast.Y))
        { selectionTimer?.Stop(); return; }
        var dx = SelectionAutoScroll.GetDelta(pointerLast.X, KeyColumnWidth, KeyColumnWidth + TimeViewportWidth, TimeSpan.FromMilliseconds(16));
        var dy = SelectionAutoScroll.GetDelta(pointerLast.Y, 0, ActualHeight, TimeSpan.FromMilliseconds(16));
        if (dx == 0 && dy == 0) { selectionTimer?.Stop(); return; }
        if (selectionTimer is null)
        {
            selectionTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
            selectionTimer.Tick += (_, _) => { var now = Stopwatch.GetTimestamp(); AdvanceRegionSelection(Stopwatch.GetElapsedTime(selectionTick, now)); selectionTick = now; };
        }
        if (!selectionTimer.IsEnabled) { selectionTick = Stopwatch.GetTimestamp(); selectionTimer.Start(); }
    }
    internal void AdvanceRegionSelection(TimeSpan elapsed)
    {
        if (!IsRectangleGesture || !CanEdit || !IsEnabled) { selectionTimer?.Stop(); return; }
        var x = Math.Clamp(HorizontalOffset + SelectionAutoScroll.GetDelta(pointerLast.X, KeyColumnWidth, KeyColumnWidth + TimeViewportWidth, elapsed), HorizontalBounds.Minimum, HorizontalBounds.Maximum);
        var y = Math.Clamp(VerticalOffset + SelectionAutoScroll.GetDelta(pointerLast.Y, 0, ActualHeight, elapsed), 0, MaximumVerticalOffset);
        if (x == HorizontalOffset && y == VerticalOffset) { selectionTimer?.Stop(); return; }
        SetCurrentValue(HorizontalOffsetProperty, x);
        SetCurrentValue(VerticalOffsetProperty, y);
        InvalidateContent();
    }
}
