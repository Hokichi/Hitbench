using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;

namespace osuHitsounder.Resources.CustomControls;

public sealed class BeatGridView : FrameworkElement
{
    private static FrameworkPropertyMetadata RenderMetadata(object? value = null) =>
        new(value, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged);

    public static readonly DependencyProperty OffsetsProperty = DependencyProperty.Register(nameof(Offsets), typeof(IReadOnlyList<int>), typeof(BeatGridView), RenderMetadata());
    public static readonly DependencyProperty BpmsProperty = DependencyProperty.Register(nameof(Bpms), typeof(IReadOnlyList<int>), typeof(BeatGridView), RenderMetadata());
    public static readonly DependencyProperty SubdivisionProperty = DependencyProperty.Register(nameof(Subdivision), typeof(int), typeof(BeatGridView), RenderMetadata(4));
    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(nameof(Duration), typeof(TimeSpan), typeof(BeatGridView), RenderMetadata(TimeSpan.Zero));
    public static readonly DependencyProperty ContentWidthProperty = DependencyProperty.Register(nameof(ContentWidth), typeof(double), typeof(BeatGridView), RenderMetadata(0d));
    public static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(BeatGridView), RenderMetadata(0d));
    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(nameof(ViewportWidth), typeof(double), typeof(BeatGridView), RenderMetadata(0d));
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(nameof(Samples), typeof(IReadOnlyList<Sample>), typeof(BeatGridView), RenderMetadata());
    public static readonly DependencyProperty SamplesRevisionProperty = DependencyProperty.Register(nameof(SamplesRevision), typeof(int), typeof(BeatGridView), RenderMetadata(0));
    public static readonly DependencyProperty CanPlaceSamplesProperty = DependencyProperty.Register(nameof(CanPlaceSamples), typeof(bool), typeof(BeatGridView), new FrameworkPropertyMetadata(false, OnCanPlaceChanged));
    public static readonly DependencyProperty HoverBrushProperty = DependencyProperty.Register(nameof(HoverBrush), typeof(Brush), typeof(BeatGridView), RenderMetadata());
    public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(nameof(MarkerBrush), typeof(Brush), typeof(BeatGridView), RenderMetadata());
    public static readonly DependencyProperty FocusBrushProperty = DependencyProperty.Register(nameof(FocusBrush), typeof(Brush), typeof(BeatGridView), RenderMetadata());
    public static readonly DependencyProperty PressedBrushProperty = DependencyProperty.Register(nameof(PressedBrush), typeof(Brush), typeof(BeatGridView), RenderMetadata());
    public static readonly RoutedEvent SamplePlacementRequestedEvent = EventManager.RegisterRoutedEvent(nameof(SamplePlacementRequested), RoutingStrategy.Bubble, typeof(EventHandler<SamplePlacementRequestedEventArgs>), typeof(BeatGridView));
    public static readonly RoutedEvent SampleRemovalRequestedEvent = EventManager.RegisterRoutedEvent(nameof(SampleRemovalRequested), RoutingStrategy.Bubble, typeof(EventHandler<SampleRemovalRequestedEventArgs>), typeof(BeatGridView));

    public IReadOnlyList<int>? Offsets { get => (IReadOnlyList<int>?)GetValue(OffsetsProperty); set => SetValue(OffsetsProperty, value); }
    public IReadOnlyList<int>? Bpms { get => (IReadOnlyList<int>?)GetValue(BpmsProperty); set => SetValue(BpmsProperty, value); }
    public int Subdivision { get => (int)GetValue(SubdivisionProperty); set => SetValue(SubdivisionProperty, value); }
    public TimeSpan Duration { get => (TimeSpan)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public double ContentWidth { get => (double)GetValue(ContentWidthProperty); set => SetValue(ContentWidthProperty, value); }
    public double HorizontalOffset { get => (double)GetValue(HorizontalOffsetProperty); set => SetValue(HorizontalOffsetProperty, value); }
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public IReadOnlyList<Sample>? Samples { get => (IReadOnlyList<Sample>?)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public int SamplesRevision { get => (int)GetValue(SamplesRevisionProperty); set => SetValue(SamplesRevisionProperty, value); }
    public bool CanPlaceSamples { get => (bool)GetValue(CanPlaceSamplesProperty); set => SetValue(CanPlaceSamplesProperty, value); }
    public Brush? HoverBrush { get => (Brush?)GetValue(HoverBrushProperty); set => SetValue(HoverBrushProperty, value); }
    public Brush? MarkerBrush { get => (Brush?)GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }
    public Brush? FocusBrush { get => (Brush?)GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }
    public Brush? PressedBrush { get => (Brush?)GetValue(PressedBrushProperty); set => SetValue(PressedBrushProperty, value); }
    public BeatInterval? ActiveInterval { get; private set; }

    public event EventHandler<SamplePlacementRequestedEventArgs> SamplePlacementRequested
    {
        add => AddHandler(SamplePlacementRequestedEvent, value);
        remove => RemoveHandler(SamplePlacementRequestedEvent, value);
    }

    public event EventHandler<SampleRemovalRequestedEventArgs> SampleRemovalRequested
    {
        add => AddHandler(SampleRemovalRequestedEvent, value);
        remove => RemoveHandler(SampleRemovalRequestedEvent, value);
    }

    private double? pointerX;
    private BeatInterval? pressedInterval;

    public BeatGridView()
    {
        FlowDirection = FlowDirection.LeftToRight;
        Focusable = true;
        Cursor = Cursors.Arrow;
        DataContextChanged += (_, _) => ClearInteraction();
        Unloaded += (_, _) => ClearInteraction();
    }

    public void UpdatePointerPosition(double? viewportX)
    {
        pointerX = viewportX;
        ActiveInterval = viewportX is { } x ? IntervalAt(x) : null;
        InvalidateVisual();
    }

    public bool RequestSamplePlacement()
    {
        if (!CanPlaceSamples || ActiveInterval is not { } interval) return false;
        RaiseEvent(new SamplePlacementRequestedEventArgs(SamplePlacementRequestedEvent, interval.StartMilliseconds));
        return true;
    }

    public bool RequestSampleRemoval(double viewportX)
    {
        if (!CanPlaceSamples || Samples is null) return false;
        for (var index = Samples.Count - 1; index >= 0; index--)
        {
            var sample = Samples[index];
            var bounds = GetSampleBounds(sample.Timestamp);
            if (bounds.IsEmpty || viewportX < bounds.Left || viewportX >= bounds.Right) continue;
            return RequestSampleRemoval(sample);
        }
        return false;
    }

    public bool RequestActiveSampleRemoval()
    {
        if (!CanPlaceSamples || Samples is null || ActiveInterval is not { } interval) return false;
        for (var index = Samples.Count - 1; index >= 0; index--)
        {
            var sample = Samples[index];
            if (sample.Timestamp < interval.StartMilliseconds || sample.Timestamp >= interval.EndMilliseconds) continue;
            return RequestSampleRemoval(sample);
        }
        return false;
    }

    public bool MoveActiveInterval(bool forward)
    {
        if (!CanPlaceSamples || Duration <= TimeSpan.Zero || ContentWidth <= 0 || Offsets is null || Bpms is null) return false;
        var visibleStart = Math.Max(0, HorizontalOffset / ContentWidth * Duration.TotalMilliseconds);
        var visibleEnd = Math.Min(Duration.TotalMilliseconds, (HorizontalOffset + Math.Max(0, ViewportWidth)) / ContentWidth * Duration.TotalMilliseconds);
        var position = ActiveInterval is { } current ? forward ? current.EndMilliseconds : current.StartMilliseconds - 0.000001 : visibleStart;
        var next = BeatGridCalculator.GetContainingInterval(Offsets, Bpms, Subdivision, position, Duration.TotalMilliseconds);
        if (ActiveInterval is null && next is { } partial && partial.StartMilliseconds < visibleStart)
            next = BeatGridCalculator.GetContainingInterval(Offsets, Bpms, Subdivision,
                partial.EndMilliseconds, Duration.TotalMilliseconds);
        if (next is null || next.Value.StartMilliseconds < visibleStart || next.Value.StartMilliseconds >= visibleEnd) return false;
        ActiveInterval = next;
        pointerX = null;
        InvalidateVisual();
        return true;
    }

    public Rect GetSampleBounds(int timestampMilliseconds)
    {
        if (Duration <= TimeSpan.Zero || ContentWidth <= 0 || ActualHeight <= 0) return Rect.Empty;
        return new Rect(timestampMilliseconds / Duration.TotalMilliseconds * ContentWidth - HorizontalOffset, 0, 20, ActualHeight);
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : 0,
        double.IsFinite(availableSize.Height) ? Math.Max(0, availableSize.Height) : 72);

    protected override AutomationPeer OnCreateAutomationPeer() => new BeatGridViewAutomationPeer(this);
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (ActiveInterval is null) MoveActiveInterval(true);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        pressedInterval = null;
        InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); UpdatePointerPosition(e.GetPosition(this).X); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); UpdatePointerPosition(null); }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!CanPlaceSamples) return;
        Focus();
        UpdatePointerPosition(e.GetPosition(this).X);
        pressedInterval = ActiveInterval;
        if (pressedInterval is not null) CaptureMouse();
        e.Handled = pressedInterval is not null;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        UpdatePointerPosition(e.GetPosition(this).X);
        var place = IsMouseCaptured && pressedInterval is { } pressed && pressed == ActiveInterval;
        ReleaseMouseCapture();
        pressedInterval = null;
        if (place) RequestSamplePlacement();
        e.Handled = place;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (!RequestSampleRemoval(e.GetPosition(this).X)) return;
        Focus();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Left or Key.Right) e.Handled = MoveActiveInterval(e.Key == Key.Right);
        else if (e.Key == Key.Enter && !e.IsRepeat) e.Handled = RequestSamplePlacement();
        else if (e.Key == Key.Delete && !e.IsRepeat) e.Handled = RequestActiveSampleRemoval();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var highContrast = SystemParameters.HighContrast;
        var hover = highContrast ? SystemColors.HighlightBrush : HoverBrush;
        var focus = highContrast ? SystemColors.HighlightTextBrush : FocusBrush;
        var pressed = highContrast ? SystemColors.HighlightTextBrush : PressedBrush;
        var marker = highContrast ? SystemColors.WindowTextBrush : MarkerBrush;
        if (ActiveInterval is { } active)
        {
            var rect = IntervalBounds(active);
            context.DrawRoundedRectangle(hover, null, rect, 4, 4);
            var cue = pressedInterval is not null ? pressed : IsKeyboardFocusWithin ? focus : null;
            if (cue is not null) context.DrawRoundedRectangle(null, new Pen(cue, 1), Inset(rect), 4, 4);
        }
        BeatGridDrawing.Draw(context, this, Offsets, Bpms, Subdivision, Duration.TotalMilliseconds,
            ContentWidth, HorizontalOffset, ViewportWidth, ActualHeight, false);
        if (marker is not null && Samples is not null)
            foreach (var sample in Samples)
            {
                var rect = GetSampleBounds(sample.Timestamp);
                if (!rect.IsEmpty) context.DrawRoundedRectangle(marker, null, Inset(rect), 4, 4);
            }
        if (IsKeyboardFocused && focus is not null) context.DrawRectangle(null, new Pen(focus, 1), Inset(new Rect(RenderSize)));
        context.Pop();
    }

    private BeatInterval? IntervalAt(double x)
    {
        if (!CanPlaceSamples || x < 0 || x >= Math.Min(ActualWidth, ViewportWidth) || Duration <= TimeSpan.Zero ||
            ContentWidth <= 0 || Offsets is null || Bpms is null) return null;
        var time = (x + HorizontalOffset) / ContentWidth * Duration.TotalMilliseconds;
        return BeatGridCalculator.GetContainingInterval(Offsets, Bpms, Subdivision, time, Duration.TotalMilliseconds);
    }

    private bool RequestSampleRemoval(Sample sample)
    {
        RaiseEvent(new SampleRemovalRequestedEventArgs(SampleRemovalRequestedEvent, sample));
        return true;
    }

    private Rect IntervalBounds(BeatInterval interval)
    {
        var scale = ContentWidth / Duration.TotalMilliseconds;
        return new Rect(interval.StartMilliseconds * scale - HorizontalOffset, 0,
            (interval.EndMilliseconds - interval.StartMilliseconds) * scale, ActualHeight);
    }

    private static Rect Inset(Rect rect) => rect.Width > 1 && rect.Height > 1
        ? new Rect(rect.X + 0.5, rect.Y + 0.5, rect.Width - 1, rect.Height - 1) : rect;

    private void ClearInteraction()
    {
        pointerX = null;
        pressedInterval = null;
        ActiveInterval = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }

    private static void OnGeometryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var view = (BeatGridView)sender;
        view.pressedInterval = null;
        if (view.pointerX is { } x) view.ActiveInterval = view.IntervalAt(x);
    }

    private static void OnCanPlaceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var view = (BeatGridView)sender;
        view.Cursor = (bool)e.NewValue ? Cursors.Hand : Cursors.Arrow;
        if (!(bool)e.NewValue) view.ClearInteraction();
    }
}
