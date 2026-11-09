using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.ComponentModel;
using System.Collections.Specialized;
using Hitbench.ViewModels.Shell;
using Hitbench.Resources.CustomControls;
using Track = Hitbench.Core.Entities.Track;

namespace Hitbench.Views.Pages;

public partial class OHsWorkstationTimeline : UserControl
{
    private static readonly DependencyPropertyKey WaveformHeightPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(WaveformHeight), typeof(double), typeof(OHsWorkstationTimeline), new PropertyMetadata(80d));
    public static readonly DependencyProperty WaveformHeightProperty = WaveformHeightPropertyKey.DependencyProperty;
    public double WaveformHeight => (double)GetValue(WaveformHeightProperty);
    public static readonly DependencyProperty IsShortLayoutProperty = DependencyProperty.Register(
        nameof(IsShortLayout), typeof(bool), typeof(OHsWorkstationTimeline), new PropertyMetadata(false,
            (owner, args) => owner.SetValue(WaveformHeightPropertyKey, (bool)args.NewValue ? 48d : 80d)));
    public bool IsShortLayout { get => (bool)GetValue(IsShortLayoutProperty); set => SetValue(IsShortLayoutProperty, value); }

    private WorkstationPageVM viewModel = null!;
    private bool deactivationStarted => viewModel is null || !viewModel.IsInteractionReady;
    private ScrollBar? scrollBar;
    private bool hasPendingZoomAnchor;
    private double zoomAnchorFraction;
    private double zoomAnchorViewportX;
    private ModifierKeys wheelMode;
    private int wheelRemainder;

    public OHsWorkstationTimeline()
    {
        InitializeComponent();
        ControlPointLane.HorizontalOffsetRequested += ScrollFromUser;
        Unloaded += (_, _) => EndScrub();
        DataContextChanged += (_, e) => { Reset(); viewModel = e.NewValue as WorkstationPageVM ?? null!; };
    }
    public event EventHandler? ViewportChanged;
    public Rect GetTimeViewportBounds(Visual relativeTo)
    {
        ArgumentNullException.ThrowIfNull(relativeTo);
        if (!IsArrangeValid || TimelineViewportWidth <= 0 || !double.IsFinite(TimelineViewportWidth) ||
            FindCommonVisualAncestor(relativeTo) is null) return Rect.Empty;
        var border = WaveformScrollViewer.BorderThickness;
        var padding = WaveformScrollViewer.Padding;
        var bounds = new Rect(border.Left + padding.Left, border.Top + padding.Top,
            TimelineViewportWidth, Math.Max(0, WaveformScrollViewer.ViewportHeight));
        return WaveformScrollViewer.TransformToVisual(relativeTo).TransformBounds(bounds);
    }
    private static readonly DependencyPropertyKey ScrollableWidthPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ScrollableWidth), typeof(double), typeof(OHsWorkstationTimeline), new PropertyMetadata(0d));
    public static readonly DependencyProperty ScrollableWidthProperty = ScrollableWidthPropertyKey.DependencyProperty;
    public double ScrollableWidth => (double)GetValue(ScrollableWidthProperty);
    public IInputElement FocusTarget => WaveformScrollViewer;
    public bool FocusTimeline() => WaveformScrollViewer.Focus();
    public void ScrollToHorizontalOffset(double offset)
    {
        if (viewModel?.IsStaticMarker == true) { PublishPresentationOffset(offset); return; }
        if (double.IsFinite(offset)) WaveformScrollViewer.ScrollToHorizontalOffset(Math.Clamp(offset, MinimumHorizontalOffset, ScrollableWidth) + TimelineEdgePadding);
    }
    public void ScrollFromUser(double offset)
    {
        if (deactivationStarted || !double.IsFinite(offset)) return;
        if (!viewModel.IsStaticMarker) viewModel.ScrollWithMarker = false;
        else
        {
            requestedOffset = Math.Clamp(offset, MinimumHorizontalOffset, ScrollableWidth);
            ScrollToHorizontalOffset(requestedOffset.Value);
            SeekViewportPosition(TimelineViewportWidth / 2, requestedOffset.Value);
            return;
        }
        ScrollToHorizontalOffset(offset);
    }
    public void AttachScrollBar(ScrollBar value)
    {
        DetachScrollBar();
        scrollBar = value;
        scrollBar.Scroll += OnWaveformScrollBarScroll;
        scrollBar.SetBinding(RangeBase.MinimumProperty, new Binding(nameof(MinimumHorizontalOffset)) { Source = this, Mode = BindingMode.OneWay });
        scrollBar.SetBinding(RangeBase.MaximumProperty, new Binding(nameof(ScrollableWidth)) { Source = this, Mode = BindingMode.OneWay });
        scrollBar.SetBinding(ScrollBar.ViewportSizeProperty, new Binding(nameof(TimelineViewportWidth)) { Source = this, Mode = BindingMode.OneWay });
        scrollBar.SetBinding(RangeBase.LargeChangeProperty, new Binding(nameof(TimelineViewportWidth)) { Source = this, Mode = BindingMode.OneWay });
        scrollBar.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnWaveformScrollBarDragStarted));
    }
    public void DetachScrollBar()
    {
        if (scrollBar is null) return;
        scrollBar.Scroll -= OnWaveformScrollBarScroll;
        scrollBar.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnWaveformScrollBarDragStarted));
        scrollBar = null;
    }
    public void ResetWheel() { wheelMode = ModifierKeys.None; wheelRemainder = 0; }
    public void Reset() { EndScrub(); ResetNavigation(); hasPendingZoomAnchor = false; ResetWheel(); ControlPointLane.ResetInteraction(); }
    public bool PreparePresentation()
    {
        var inset = WaveformScrollViewer.BorderThickness;
        var padding = WaveformScrollViewer.Padding;
        var hasSpace = WaveformScrollViewer.ActualWidth > inset.Left + inset.Right + padding.Left + padding.Right;
        if ((hasSpace && WaveformScrollViewer.ViewportWidth <= 0) || !WaveformDisplay.IsMeasureValid || !WaveformDisplay.IsArrangeValid) return false;
        WaveformDisplay.PrepareDrawing();
        return true;
    }
    private static readonly DependencyPropertyKey TimelineContentWidthPropertyKey = DependencyProperty.RegisterReadOnly(nameof(TimelineContentWidth), typeof(double), typeof(OHsWorkstationTimeline), new PropertyMetadata(0d));
    public static readonly DependencyProperty TimelineContentWidthProperty = TimelineContentWidthPropertyKey.DependencyProperty;
    public double TimelineContentWidth { get => (double)GetValue(TimelineContentWidthProperty); private set => SetValue(TimelineContentWidthPropertyKey, value); }
    private static readonly DependencyPropertyKey TimelineViewportWidthPropertyKey = DependencyProperty.RegisterReadOnly(nameof(TimelineViewportWidth), typeof(double), typeof(OHsWorkstationTimeline), new PropertyMetadata(0d));
    public static readonly DependencyProperty TimelineViewportWidthProperty = TimelineViewportWidthPropertyKey.DependencyProperty;
    public double TimelineViewportWidth { get => (double)GetValue(TimelineViewportWidthProperty); private set => SetValue(TimelineViewportWidthPropertyKey, value); }
    private static readonly DependencyPropertyKey TimelineHorizontalOffsetPropertyKey = DependencyProperty.RegisterReadOnly(nameof(TimelineHorizontalOffset), typeof(double), typeof(OHsWorkstationTimeline), new PropertyMetadata(0d));
    public static readonly DependencyProperty TimelineHorizontalOffsetProperty = TimelineHorizontalOffsetPropertyKey.DependencyProperty;
    public double TimelineHorizontalOffset { get => (double)GetValue(TimelineHorizontalOffsetProperty); private set => SetValue(TimelineHorizontalOffsetPropertyKey, value); }
    private void OnWaveformViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Bootstrap measurement before the ScrollViewer has a nonzero viewport.
        var inset = WaveformScrollViewer.BorderThickness;
        var padding = WaveformScrollViewer.Padding;
        WaveformDisplay.SetCurrentValue(WaveformView.ViewportWidthProperty,
            Math.Max(0, e.NewSize.Width - inset.Left - inset.Right - padding.Left - padding.Right));
        TimelineViewportWidth = WaveformScrollViewer.ViewportWidth;
    }
    private void OnWaveformDisplaySizeChanged(object sender, SizeChangedEventArgs e)
    {
        TimelineContentWidth = e.NewSize.Width;
        RefreshTimelineGeometry();
        ApplyPendingZoomAnchor();
        if (viewModel?.IsStaticMarker == true) CenterPlaybackPosition();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }
    private void OnWaveformScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var inset = WaveformScrollViewer.BorderThickness;
        var padding = WaveformScrollViewer.Padding;
        ControlPointLane.Margin = new Thickness(inset.Left + padding.Left, ControlPointLane.Margin.Top, inset.Right + padding.Right, ControlPointLane.Margin.Bottom);
        if (e.ViewportWidth > 0) WaveformDisplay.SetCurrentValue(WaveformView.ViewportWidthProperty, e.ViewportWidth);
        var viewportChanged = e.ViewportWidthChange != 0;
        TimelineViewportWidth = e.ViewportWidth;
        RefreshTimelineGeometry();
        WaveformDisplay.LayoutHorizontalOffset = e.HorizontalOffset - TimelineEdgePadding;
        if (viewModel?.IsStaticMarker != true) TimelineHorizontalOffset = e.HorizontalOffset - TimelineEdgePadding;
        if (!navigating && requestedOffset is { } requested && Math.Abs(requested - TimelineHorizontalOffset) < .01) requestedOffset = null;
        if (scrollBar is not null) scrollBar.Value = TimelineHorizontalOffset;
        ApplyPendingZoomAnchor();
        if (viewportChanged && viewModel?.IsStaticMarker == true) CenterPlaybackPosition();
        ViewportChanged?.Invoke(this, EventArgs.Empty);

    }

    private void OnWaveformScrollBarDragStarted(object sender, DragStartedEventArgs e)
    {
        if (viewModel.CanPlay && !deactivationStarted && !viewModel.IsStaticMarker) viewModel.ScrollWithMarker = false;
    }

    private void OnWaveformScrollBarScroll(object sender, ScrollEventArgs e) => ScrollFromUser(e.NewValue);

    public void ApplyPendingZoomAnchor()
    {
        if (!hasPendingZoomAnchor || deactivationStarted) return;
        var extent = WaveformDisplay.ActualWidth;
        var viewport = WaveformScrollViewer.ViewportWidth;
        if (!double.IsFinite(extent) || extent <= 0 || !double.IsFinite(viewport) || viewport <= 0) return;
        var expectedExtent = WaveformDisplay.ViewportWidth * viewModel.ZoomFactor;
        if (Math.Abs(extent - expectedExtent) > 1 || Math.Abs(WaveformScrollViewer.ExtentWidth - extent - 2 * TimelineEdgePadding) > 1) return;

        var offset = Math.Clamp(zoomAnchorFraction * extent - zoomAnchorViewportX, MinimumHorizontalOffset, ScrollableWidth);
        hasPendingZoomAnchor = false;
        ScrollToHorizontalOffset(offset);
    }

    private void OnWaveformMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        WaveformScrollViewer.Focus();
        if (!BeginScrub(ScrubPointer(e))) return;
        e.Handled = true;
    }

    public void CaptureZoomAnchor()
    {
        if (hasPendingZoomAnchor) return;
        var duration = viewModel.Waveform?.Duration.TotalSeconds ?? 0;
        var extent = WaveformDisplay.ActualWidth;
        var viewport = WaveformScrollViewer.ViewportWidth;
        if (!double.IsFinite(duration) || duration <= 0 || !double.IsFinite(extent) || extent <= 0 ||
            !double.IsFinite(viewport) || viewport <= 0) return;

        zoomAnchorFraction = Math.Clamp(viewModel.Position.TotalSeconds / duration, 0, 1);
        var markerViewportX = zoomAnchorFraction * extent - TimelineHorizontalOffset;
        if (viewModel.IsStaticMarker) { zoomAnchorViewportX = viewport / 2; hasPendingZoomAnchor = true; return; }
        zoomAnchorViewportX = markerViewportX >= 0 && markerViewportX <= viewport
            ? markerViewportX
            : viewport / 2;
        hasPendingZoomAnchor = true;
    }
    public void RevealPlaybackPosition()
    {
        if (viewModel.IsStaticMarker) { if (!navigating) requestedOffset = null; CenterPlaybackPosition(); return; }
        var duration = viewModel.Waveform?.Duration.TotalSeconds ?? 0;
        var extent = WaveformDisplay.ActualWidth;
        var viewport = WaveformScrollViewer.ViewportWidth;
        if (duration <= 0 || !double.IsFinite(extent) || extent <= 0 ||
            !double.IsFinite(viewport) || viewport <= 0) return;
        var x = Math.Clamp(viewModel.Position.TotalSeconds / duration * extent, 0, extent);
        var offset = WaveformScrollViewer.HorizontalOffset;
        if (x < offset) WaveformScrollViewer.ScrollToHorizontalOffset(x);
        else if (x > offset + viewport)
            WaveformScrollViewer.ScrollToHorizontalOffset(Math.Min(WaveformScrollViewer.ScrollableWidth, x - viewport));
    }

    public void FollowPlaybackPosition()
    {
        if (viewModel.IsStaticMarker) { if (!navigating) requestedOffset = null; CenterPlaybackPosition(); return; }
        if (!viewModel.ScrollWithMarker || deactivationStarted) return;
        var duration = viewModel.Waveform?.Duration.TotalSeconds ?? 0;
        if (duration <= 0) return;
        var offset = GetPlaybackPageOffset(viewModel.Position.TotalSeconds / duration * WaveformDisplay.ActualWidth,
            WaveformScrollViewer.HorizontalOffset, WaveformScrollViewer.ViewportWidth, WaveformDisplay.ActualWidth);
        if (offset != WaveformScrollViewer.HorizontalOffset) WaveformScrollViewer.ScrollToHorizontalOffset(offset);
    }

    internal static double GetPlaybackPageOffset(double markerX, double offset, double viewportWidth, double extentWidth)
    {
        if (!double.IsFinite(markerX) || !double.IsFinite(offset) || !double.IsFinite(viewportWidth) ||
            !double.IsFinite(extentWidth) || viewportWidth <= 0 || extentWidth <= 0) return offset;
        if (markerX >= offset && markerX < offset + viewportWidth) return offset;
        var pages = Math.Floor((markerX - offset) / viewportWidth);
        return Math.Clamp(offset + pages * viewportWidth, 0, Math.Max(0, extentWidth - viewportWidth));
    }

    public bool HandleWaveformWheel(ModifierKeys modifiers, int delta)
    {
        if (modifiers is not ModifierKeys.None and not ModifierKeys.Control and not (ModifierKeys.Control | ModifierKeys.Alt) and not ModifierKeys.Shift and
            not (ModifierKeys.Control | ModifierKeys.Shift) ||
            !viewModel.CanZoom || deactivationStarted)
        {
            wheelMode = ModifierKeys.None;
            wheelRemainder = 0;
            return false;
        }

        if (wheelMode != modifiers)
        {
            wheelMode = modifiers;
            wheelRemainder = 0;
        }

        wheelRemainder += delta;
        var steps = wheelRemainder / Mouse.MouseWheelDeltaForOneLine;
        wheelRemainder %= Mouse.MouseWheelDeltaForOneLine;
        if (modifiers is ModifierKeys.Shift or ModifierKeys.None)
        {
            if (delta != 0 && !viewModel.IsStaticMarker) viewModel.ScrollWithMarker = false;
            if (viewModel.IsStaticMarker)
            {
                if (steps != 0) ScrollFromUser(UserHorizontalOffset - steps * 16);
            }
            else for (var index = 0; index < Math.Abs(steps); index++)
            {
                if (steps > 0) WaveformScrollViewer.LineLeft(); else WaveformScrollViewer.LineRight();
            }
            return true;
        }

        var increase = steps > 0;
        for (var index = 0; index < Math.Abs(steps); index++)
        {
            var command = modifiers switch
            {
                ModifierKeys.Control => increase ? viewModel.ZoomInCommand : viewModel.ZoomOutCommand,
                (ModifierKeys.Control | ModifierKeys.Alt) => increase ? viewModel.IncreaseSubdivisionCommand : viewModel.DecreaseSubdivisionCommand,
                _ => increase ? viewModel.IncreaseTrackHeightCommand : viewModel.DecreaseTrackHeightCommand
            };
            if (!command.CanExecute(null)) break;
            command.Execute(null);
        }
        return true;
    }
}
