using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using Hitbench.Core.Entities;
using Hitbench.Services;

namespace Hitbench.Resources.CustomControls;

public sealed partial class BeatGridView : FrameworkElement
{
    public static readonly DependencyProperty IsPlaybackMarkerStaticProperty = DependencyProperty.Register(nameof(IsPlaybackMarkerStatic), typeof(bool), typeof(BeatGridView), RenderMetadata(false));
    public bool IsPlaybackMarkerStatic { get => (bool)GetValue(IsPlaybackMarkerStaticProperty); set => SetValue(IsPlaybackMarkerStaticProperty, value); }
    public static readonly DependencyProperty ZoomFactorProperty = DependencyProperty.Register(nameof(ZoomFactor), typeof(double), typeof(BeatGridView), RenderMetadata(32d));
    public static readonly DependencyProperty DragPreviewTimestampsProperty = DependencyProperty.Register(nameof(DragPreviewTimestamps), typeof(IReadOnlyList<int>), typeof(BeatGridView), RenderMetadata());
    public double ZoomFactor { get => (double)GetValue(ZoomFactorProperty); set => SetValue(ZoomFactorProperty, value); }
    public IReadOnlyList<int>? DragPreviewTimestamps { get => (IReadOnlyList<int>?)GetValue(DragPreviewTimestampsProperty); set => SetValue(DragPreviewTimestampsProperty, value); }
    private static FrameworkPropertyMetadata RenderMetadata(object? value = null) =>
        new(value, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged);

    public static readonly DependencyProperty UninheritedPointsProperty = DependencyProperty.Register(nameof(UninheritedPoints), typeof(IReadOnlyList<UninheritedPoint>), typeof(BeatGridView), RenderMetadata());
    public static readonly DependencyProperty SubdivisionProperty = DependencyProperty.Register(nameof(Subdivision), typeof(int), typeof(BeatGridView), RenderMetadata(4));
    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(nameof(Duration), typeof(TimeSpan), typeof(BeatGridView), RenderMetadata(TimeSpan.Zero));
    public static readonly DependencyProperty ContentWidthProperty = DependencyProperty.Register(nameof(ContentWidth), typeof(double), typeof(BeatGridView), RenderMetadata(0d));
    public static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(BeatGridView), RenderMetadata(0d));
    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(nameof(ViewportWidth), typeof(double), typeof(BeatGridView), RenderMetadata(0d));
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(nameof(Samples), typeof(IReadOnlyList<Sample>), typeof(BeatGridView), RenderMetadata());
    public static readonly DependencyProperty SamplesRevisionProperty = DependencyProperty.Register(nameof(SamplesRevision), typeof(int), typeof(BeatGridView), RenderMetadata(0));
    public static readonly DependencyProperty AllowNodeCreationProperty = DependencyProperty.Register(nameof(AllowNodeCreation), typeof(bool), typeof(BeatGridView), RenderMetadata(true));
    public bool AllowNodeCreation { get => (bool)GetValue(AllowNodeCreationProperty); set => SetValue(AllowNodeCreationProperty, value); }
    public static readonly DependencyProperty CanPlaceSamplesProperty = DependencyProperty.Register(nameof(CanPlaceSamples), typeof(bool), typeof(BeatGridView), new FrameworkPropertyMetadata(false, OnCanPlaceChanged));
    public static readonly DependencyProperty HoverBrushProperty = DependencyProperty.Register(nameof(HoverBrush), typeof(Brush), typeof(BeatGridView), RenderMetadata());
    public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(nameof(MarkerBrush), typeof(Brush), typeof(BeatGridView), RenderMetadata());
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(TimeSpan), typeof(BeatGridView),
        new FrameworkPropertyMetadata(TimeSpan.Zero, (sender, _) => ((BeatGridView)sender).UpdateMarker()));
    public static readonly DependencyProperty PlaybackMarkerBrushProperty = DependencyProperty.Register(nameof(PlaybackMarkerBrush), typeof(Brush), typeof(BeatGridView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PlaybackMarkerOutlineBrushProperty = DependencyProperty.Register(nameof(PlaybackMarkerOutlineBrush), typeof(Brush), typeof(BeatGridView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FocusBrushProperty = DependencyProperty.Register(nameof(FocusBrush), typeof(Brush), typeof(BeatGridView), RenderMetadata());
    public static readonly DependencyProperty PressedBrushProperty = DependencyProperty.Register(nameof(PressedBrush), typeof(Brush), typeof(BeatGridView), RenderMetadata());
    public static readonly DependencyProperty SelectionBrushProperty = DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(BeatGridView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SelectionCueBrushProperty = DependencyProperty.Register(nameof(SelectionCueBrush), typeof(Brush), typeof(BeatGridView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SelectedSamplesProperty = DependencyProperty.Register(nameof(SelectedSamples), typeof(IReadOnlyCollection<Sample>), typeof(BeatGridView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsNodeDragActiveProperty = DependencyProperty.Register(nameof(IsNodeDragActive), typeof(bool), typeof(BeatGridView), RenderMetadata(false));
    public static readonly DependencyProperty IsNodeDragCopyProperty = DependencyProperty.Register(nameof(IsNodeDragCopy), typeof(bool), typeof(BeatGridView), RenderMetadata(false));
    public static readonly DependencyProperty PressedSampleProperty = DependencyProperty.Register(nameof(PressedSample), typeof(Sample), typeof(BeatGridView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SuppressPlacementFeedbackProperty = DependencyProperty.Register(nameof(SuppressPlacementFeedback), typeof(bool), typeof(BeatGridView), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly RoutedEvent SamplePlacementRequestedEvent = EventManager.RegisterRoutedEvent(nameof(SamplePlacementRequested), RoutingStrategy.Bubble, typeof(EventHandler<SamplePlacementRequestedEventArgs>), typeof(BeatGridView));
    public static readonly RoutedEvent SampleRemovalRequestedEvent = EventManager.RegisterRoutedEvent(nameof(SampleRemovalRequested), RoutingStrategy.Bubble, typeof(EventHandler<SampleRemovalRequestedEventArgs>), typeof(BeatGridView));

    public static readonly RoutedEvent SampleSelectionRequestedEvent = EventManager.RegisterRoutedEvent(nameof(SampleSelectionRequested), RoutingStrategy.Bubble, typeof(EventHandler<SampleSelectionRequestedEventArgs>), typeof(BeatGridView));
    public event EventHandler<SampleSelectionRequestedEventArgs> SampleSelectionRequested
    { add => AddHandler(SampleSelectionRequestedEvent, value); remove => RemoveHandler(SampleSelectionRequestedEvent, value); }
    public IReadOnlyList<UninheritedPoint>? UninheritedPoints { get => (IReadOnlyList<UninheritedPoint>?)GetValue(UninheritedPointsProperty); set => SetValue(UninheritedPointsProperty, value); }
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
    public TimeSpan Position { get => (TimeSpan)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public Brush? PlaybackMarkerBrush { get => (Brush?)GetValue(PlaybackMarkerBrushProperty); set => SetValue(PlaybackMarkerBrushProperty, value); }
    public Brush? PlaybackMarkerOutlineBrush { get => (Brush?)GetValue(PlaybackMarkerOutlineBrushProperty); set => SetValue(PlaybackMarkerOutlineBrushProperty, value); }
    public Brush? FocusBrush { get => (Brush?)GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }
    public Brush? PressedBrush { get => (Brush?)GetValue(PressedBrushProperty); set => SetValue(PressedBrushProperty, value); }
    public Brush? SelectionBrush { get => (Brush?)GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }
    public Brush? SelectionCueBrush { get => (Brush?)GetValue(SelectionCueBrushProperty); set => SetValue(SelectionCueBrushProperty, value); }
    public IReadOnlyCollection<Sample>? SelectedSamples { get => (IReadOnlyCollection<Sample>?)GetValue(SelectedSamplesProperty); set => SetValue(SelectedSamplesProperty, value); }
    public bool IsNodeDragActive { get => (bool)GetValue(IsNodeDragActiveProperty); set => SetValue(IsNodeDragActiveProperty, value); }
    public bool IsNodeDragCopy { get => (bool)GetValue(IsNodeDragCopyProperty); set => SetValue(IsNodeDragCopyProperty, value); }
    public Sample? PressedSample { get => (Sample?)GetValue(PressedSampleProperty); set => SetValue(PressedSampleProperty, value); }
    public bool SuppressPlacementFeedback { get => (bool)GetValue(SuppressPlacementFeedbackProperty); set => SetValue(SuppressPlacementFeedbackProperty, value); }
    public BeatInterval? ActiveInterval { get; private set; }
    internal bool HasPlacementPreview => IsEnabled && CanPlaceSamples && AllowNodeCreation &&
        !IsNodeDragActive && !SuppressPlacementFeedback && Duration > TimeSpan.Zero &&
        double.IsFinite(ContentWidth) && ContentWidth > 0 && ActualWidth > 0 && ActualHeight > 0 &&
        double.IsFinite(HorizontalOffset) && double.IsFinite(ViewportWidth) && ViewportWidth > 0 &&
        ActiveInterval is { } interval &&
        Math.Round(interval.StartMilliseconds, MidpointRounding.AwayFromZero) < Duration.TotalMilliseconds &&
        FindSampleInInterval(interval) is null;

    private readonly SampleTimeIndex sampleIndex = new();
    internal void RefreshSampleIndex() => sampleIndex.Invalidate();
    private IReadOnlyList<Sample> SamplesInRange(double left, double right)
    {
        if (Duration <= TimeSpan.Zero || !double.IsFinite(ContentWidth) || ContentWidth <= 0) return [];
        sampleIndex.Update(Samples, SamplesRevision);
        var half = OHsSampleNodeDrawing.GetBounds(0, Duration, ContentWidth, 0, 0, 40, ZoomFactor).Width / 2;
        return sampleIndex.Query((left + HorizontalOffset - half) / ContentWidth * Duration.TotalMilliseconds,
            (right + HorizontalOffset + half) / ContentWidth * Duration.TotalMilliseconds);
    }
    private IReadOnlyList<Sample> SamplesAtTimes(double minimum, double maximum)
    {
        sampleIndex.Update(Samples, SamplesRevision);
        return sampleIndex.Query(minimum, maximum);
    }
    private HashSet<Sample> selectedMembership = new(ReferenceEqualityComparer.Instance);
    private bool IsSampleSelected(Sample sample) => selectedMembership.Contains(sample);
    private bool IsSampleHidden(Sample sample) => IsNodeDragActive && !IsNodeDragCopy && IsSampleSelected(sample);

    private IEnumerable<Sample> SamplesInHitOrder(IReadOnlyList<Sample>? candidates = null)
    {
        var items = candidates ?? Samples;
        if (items is null) yield break;
        for (var layer = 0; layer < 2; layer++)
            for (var index = items.Count - 1; index >= 0; index--)
            {
                var sample = items[index];
                if (IsSampleSelected(sample) == (layer == 0) && !IsSampleHidden(sample)) yield return sample;
            }
    }

    public Rect GetDisplayedSampleBounds(Sample sample) => IsSampleHidden(sample)
        ? Rect.Empty : GetSampleBounds(sample.Timestamp);

    private Rect SampleViewport => new(0, 0, Math.Max(0, Math.Min(ActualWidth, ViewportWidth)), Math.Max(0, ActualHeight));

    public Sample? FindSampleAt(Point point)
    {
        if (!CanPlaceSamples || Samples is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
            point.X < 0 || point.X >= SampleViewport.Width || point.Y < 0 || point.Y >= ActualHeight) return null;
        foreach (var sample in SamplesInHitOrder(SamplesInRange(point.X, point.X)))
        {
            var bounds = GetDisplayedSampleBounds(sample);
            if (!bounds.IsEmpty && point.X >= bounds.Left && point.X < bounds.Right &&
                point.Y >= bounds.Top && point.Y < bounds.Bottom) return sample;
        }
        return null;
    }

    public IReadOnlyList<Sample> GetSamplesIntersecting(Rect selection)
    {
        if (!CanPlaceSamples || Samples is null || selection.IsEmpty) return [];
        selection.Intersect(SampleViewport);
        if (selection.IsEmpty || selection.Width <= 0 || selection.Height <= 0) return [];
        return SamplesInRange(selection.Left, selection.Right).Where(sample => GetDisplayedSampleBounds(sample).IntersectsWith(selection)).ToArray();
    }

    public Sample? FindActiveSample()
    {
        if (pointerX is { } x && FindSampleAt(new Point(x, ActualHeight / 2)) is { } hit) return hit;
        if (!CanPlaceSamples || Samples is null || ActiveInterval is not { } interval) return null;
        return FindSampleInInterval(interval);
    }

    private Sample? FindSampleInInterval(BeatInterval interval)
    {
        var target = Math.Round(interval.StartMilliseconds, MidpointRounding.AwayFromZero);
        var nextTarget = interval.EndMilliseconds < Duration.TotalMilliseconds
            ? Math.Round(interval.EndMilliseconds, MidpointRounding.AwayFromZero) : double.NaN;
        foreach (var sample in SamplesInHitOrder(SamplesAtTimes(Math.Min(target, interval.StartMilliseconds), Math.Max(target, interval.EndMilliseconds))))
        {
            var timestamp = sample.Timestamp;
            if (timestamp == target || timestamp >= interval.StartMilliseconds && timestamp < interval.EndMilliseconds && timestamp != nextTarget) return sample;
        }
        return null;
    }

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
        InitializeDrawing();
        FlowDirection = FlowDirection.LeftToRight;
        Focusable = true;
        Cursor = Cursors.Arrow;
        DataContextChanged += (_, _) => ClearInteraction();
        Unloaded += (_, _) => ClearInteraction();
        IsEnabledChanged += (_, _) => { if (!IsEnabled) ClearInteraction(); };
    }

    public void UpdatePointerPosition(double? viewportX)
    {
        pointerX = viewportX;
        ActiveInterval = viewportX is { } x ? IntervalAt(x) : null;
        InvalidateFeedback();
    }

    public bool RequestSamplePlacement()
    {
        if (!IsEnabled || !CanPlaceSamples || IsNodeDragActive || ActiveInterval is not { } interval) return false;
        if (!HasPlacementPreview && FindActiveSample() is { } sample) RaiseEvent(new SampleSelectionRequestedEventArgs(SampleSelectionRequestedEvent, sample));
        else if (!AllowNodeCreation) return false;
        else RaiseEvent(new SamplePlacementRequestedEventArgs(SamplePlacementRequestedEvent, interval.StartMilliseconds));
        return true;
    }

    internal bool RequestPointerSamplePlacement()
    {
        if (pointerX is { } x && FindSampleAt(new Point(x, ActualHeight / 2)) is null && FindActiveSample() is not null) return true;
        return RequestSamplePlacement();
    }

    public bool RequestSampleRemoval(double viewportX)
    {
        if (!CanPlaceSamples || Samples is null || viewportX < 0 || viewportX >= Math.Min(ActualWidth, ViewportWidth)) return false;
        foreach (var sample in SamplesInHitOrder(SamplesInRange(viewportX, viewportX)))
        {
            var bounds = GetDisplayedSampleBounds(sample);
            if (bounds.IsEmpty || viewportX < bounds.Left || viewportX >= bounds.Right) continue;
            return RequestSampleRemoval(sample);
        }
        return false;
    }

    public bool RequestActiveSampleRemoval()
    {
        if (!CanPlaceSamples || Samples is null || ActiveInterval is not { } interval) return false;
        foreach (var sample in SamplesInHitOrder(SamplesAtTimes(interval.StartMilliseconds, interval.EndMilliseconds)))
        {
            var timestamp = sample.Timestamp;
            if (timestamp >= interval.StartMilliseconds && timestamp < interval.EndMilliseconds) return RequestSampleRemoval(sample);
        }
        return false;
    }

    public bool MoveActiveInterval(bool forward)
    {
        if (!CanPlaceSamples || Duration <= TimeSpan.Zero || ContentWidth <= 0 || UninheritedPoints is null) return false;
        var visibleStart = Math.Max(0, HorizontalOffset / ContentWidth * Duration.TotalMilliseconds);
        var visibleEnd = Math.Min(Duration.TotalMilliseconds, (HorizontalOffset + Math.Max(0, ViewportWidth)) / ContentWidth * Duration.TotalMilliseconds);
        var position = ActiveInterval is { } current ? forward ? current.EndMilliseconds : current.StartMilliseconds - 0.000001 : visibleStart;
        var next = BeatGridCalculator.GetContainingInterval(UninheritedPoints, Subdivision, position, Duration.TotalMilliseconds);
        if (ActiveInterval is null && next is { } partial && partial.StartMilliseconds < visibleStart)
            next = BeatGridCalculator.GetContainingInterval(UninheritedPoints, Subdivision,
                partial.EndMilliseconds, Duration.TotalMilliseconds);
        if (next is null || next.Value.StartMilliseconds < visibleStart || next.Value.StartMilliseconds >= visibleEnd) return false;
        ActiveInterval = next;
        pointerX = null;
        InvalidateContent();
        return true;
    }

    public void SetActivePosition(TimeSpan position) => SetActivePosition(position.TotalMilliseconds);

    internal void SetActivePosition(double positionMilliseconds)
    {
        if (!CanPlaceSamples || UninheritedPoints is null) return;
        ActiveInterval = BeatGridCalculator.GetContainingInterval(UninheritedPoints, Subdivision,
            positionMilliseconds, Duration.TotalMilliseconds);
        pointerX = null;
        pressedInterval = null;
        InvalidateContent();
    }

    public Rect GetSampleBounds(int timestampMilliseconds)
    {
        return OHsSampleNodeDrawing.GetBounds(timestampMilliseconds,Duration,ContentWidth,HorizontalOffset,0,ActualHeight,ZoomFactor);
    }

    public double? GetPlaybackMarkerX()
    {
        var viewport = Math.Min(ActualWidth, ViewportWidth);
        if (Duration <= TimeSpan.Zero || !double.IsFinite(ContentWidth) || ContentWidth <= 0 ||
            !double.IsFinite(HorizontalOffset) || !double.IsFinite(viewport) || viewport <= 0) return null;
        var time = Math.Clamp(Position.TotalMilliseconds, 0, Duration.TotalMilliseconds);
        if (IsPlaybackMarkerStatic) return viewport / 2;
        var x = time / Duration.TotalMilliseconds * ContentWidth - HorizontalOffset;
        if (x < 0 || x > viewport) return null;
        return Math.Clamp(x, 0.75, Math.Max(0.75, viewport - 0.75));
    }


    private readonly DrawingVisual feedbackVisual = new();
    private bool feedbackDirty = true;
    internal long ContentBuildCount { get; private set; }
    private readonly DrawingVisual contentVisual = new();
    private readonly DrawingVisual markerVisual = new();
    private readonly TranslateTransform markerTransform = new();
    private readonly RectangleGeometry markerClip = new();
    private bool contentDirty = true;
    internal DrawingGroup? ContentDrawing { get; private set; }
    protected override int VisualChildrenCount => 3;
    protected override Visual GetVisualChild(int index) => index switch
    { 0 => contentHost, 1 => feedbackVisual, 2 => markerVisual, _ => throw new ArgumentOutOfRangeException(nameof(index)) };

    private void InitializeDrawing()
    {
        contentHost.Children.Add(contentVisual); contentHost.Clip = contentClip;
        contentVisual.Transform = contentTransform;
        AddVisualChild(contentHost);
        AddVisualChild(feedbackVisual);
        AddVisualChild(markerVisual);
        markerVisual.Transform = markerTransform;
        markerVisual.Clip = markerClip;
        Loaded += (_, _) => { SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged; SystemParameters.StaticPropertyChanged += OnSystemSettingsChanged; InvalidateContent(); };
        Unloaded += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged;
    }

    private void OnSystemSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) InvalidateContent();
        else Dispatcher.BeginInvoke(InvalidateContent);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == HorizontalOffsetProperty) { UpdateViewport(); InvalidateFeedback(); UpdateMarker(); return; }
        if (e.Property == SelectedSamplesProperty)
            selectedMembership = new HashSet<Sample>(SelectedSamples ?? [], ReferenceEqualityComparer.Instance);
        if (e.Property != PositionProperty && e.Property.GetMetadata(GetType()) is FrameworkPropertyMetadata metadata &&
            (metadata.AffectsRender || metadata.AffectsMeasure)) InvalidateContent();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    { base.OnRenderSizeChanged(info); InvalidateContent(); }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    { base.OnDpiChanged(oldDpi, newDpi); InvalidateContent(); }
    private void InvalidateContent() { retainedViewport.Invalidate(); contentDirty = feedbackDirty = true; InvalidateVisual(); }
    private void InvalidateFeedback() { feedbackDirty = true; InvalidateVisual(); }

    internal void PrepareDrawing()
    {
        UpdateViewport();
        if (feedbackDirty)
        {
            using var feedback = feedbackVisual.RenderOpen();
            DrawFeedback(feedback);
            feedbackDirty = false;
        }
        if (!contentDirty) return;
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) DrawContent(context);
        ContentDrawing = drawing;
        using (var context = contentVisual.RenderOpen()) context.DrawDrawing(drawing);
        using (var context = markerVisual.RenderOpen()) DrawMarker(context);
        contentDirty = false; ContentBuildCount++;
        UpdateMarker();
    }

    protected override void OnRender(DrawingContext context) { base.OnRender(context); PrepareDrawing(); }

    private void DrawMarker(DrawingContext context)
    {
        var brush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : PlaybackMarkerBrush;
        if (brush is null) return;
        var end = new Point(0, ActualHeight);
        if (!SystemParameters.HighContrast && PlaybackMarkerOutlineBrush is not null)
            context.DrawLine(new Pen(PlaybackMarkerOutlineBrush, 3.5), new Point(), end);
        context.DrawLine(new Pen(brush, 1.5), new Point(), end);
    }

    private void UpdateMarker()
    {
        var x = GetPlaybackMarkerX();
        markerVisual.Opacity = x.HasValue ? 1 : 0;
        markerTransform.X = x ?? 0;
        markerClip.Rect = new Rect(-(x ?? 0), 0, ActualWidth, ActualHeight);
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : 0,
        double.IsFinite(availableSize.Height) ? Math.Max(0, availableSize.Height) : 36);

    protected override AutomationPeer OnCreateAutomationPeer() => new BeatGridViewAutomationPeer(this);
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (ActiveInterval is null) MoveActiveInterval(true);
        InvalidateContent();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateContent();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        pressedInterval = null;
        InvalidateContent();
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
        if (place) RequestPointerSamplePlacement();
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

    }


    private BeatInterval? IntervalAt(double x)
    {
        if (!CanPlaceSamples || x < 0 || x >= Math.Min(ActualWidth, ViewportWidth) || Duration <= TimeSpan.Zero ||
            ContentWidth <= 0 || UninheritedPoints is null) return null;
        var time = (x + HorizontalOffset) / ContentWidth * Duration.TotalMilliseconds;
        return BeatGridCalculator.GetContainingInterval(UninheritedPoints, Subdivision, time, Duration.TotalMilliseconds);
    }

    private bool RequestSampleRemoval(Sample sample)
    {
        RaiseEvent(new SampleRemovalRequestedEventArgs(SampleRemovalRequestedEvent, sample));
        return true;
    }

    private static Rect Inset(Rect rect) => OHsSampleNodeDrawing.Inset(rect);

    private void ClearInteraction()
    {
        pointerX = null;
        pressedInterval = null;
        SetCurrentValue(PressedSampleProperty, null);
        SetCurrentValue(DragPreviewTimestampsProperty, null);
        ActiveInterval = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateContent();
    }

    private static void OnGeometryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var view = (BeatGridView)sender;
        if (e.Property == HorizontalOffsetProperty) { if (view.pointerX is { } pointer) view.ActiveInterval = view.IntervalAt(pointer); return; }
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
