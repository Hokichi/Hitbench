using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation.Peers;
using osuHitsounder.Core.Constants;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
namespace osuHitsounder.Resources.CustomControls;

public sealed class PianoRollView : Control
{
    public static readonly DependencyProperty NotesProperty = DependencyProperty.Register(nameof(Notes), typeof(IReadOnlyList<InstrumentalNoteSnapshot>), typeof(PianoRollView), new FrameworkPropertyMetadata(Array.Empty<InstrumentalNoteSnapshot>(), FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public IReadOnlyList<InstrumentalNoteSnapshot> Notes { get => (IReadOnlyList<InstrumentalNoteSnapshot>)GetValue(NotesProperty); set => SetValue(NotesProperty, value); }
    public static readonly DependencyProperty SelectedNoteIdProperty = DependencyProperty.Register(nameof(SelectedNoteId), typeof(Guid?), typeof(PianoRollView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public Guid? SelectedNoteId { get => (Guid?)GetValue(SelectedNoteIdProperty); set => SetValue(SelectedNoteIdProperty, value); }
    public static readonly DependencyProperty OffsetsProperty = DependencyProperty.Register(nameof(Offsets), typeof(IReadOnlyList<int>), typeof(PianoRollView), new FrameworkPropertyMetadata(Array.Empty<int>(), FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public IReadOnlyList<int> Offsets { get => (IReadOnlyList<int>)GetValue(OffsetsProperty); set => SetValue(OffsetsProperty, value); }
    public static readonly DependencyProperty BpmsProperty = DependencyProperty.Register(nameof(Bpms), typeof(IReadOnlyList<int>), typeof(PianoRollView), new FrameworkPropertyMetadata(Array.Empty<int>(), FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public IReadOnlyList<int> Bpms { get => (IReadOnlyList<int>)GetValue(BpmsProperty); set => SetValue(BpmsProperty, value); }
    public static readonly DependencyProperty SubdivisionProperty = DependencyProperty.Register(nameof(Subdivision), typeof(int), typeof(PianoRollView), new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public int Subdivision { get => (int)GetValue(SubdivisionProperty); set => SetValue(SubdivisionProperty, value); }
    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(nameof(Duration), typeof(TimeSpan), typeof(PianoRollView), new FrameworkPropertyMetadata(TimeSpan.Zero, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public TimeSpan Duration { get => (TimeSpan)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(nameof(Position), typeof(TimeSpan), typeof(PianoRollView), new FrameworkPropertyMetadata(TimeSpan.Zero, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public TimeSpan Position { get => (TimeSpan)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public static readonly DependencyProperty ContentWidthProperty = DependencyProperty.Register(nameof(ContentWidth), typeof(double), typeof(PianoRollView), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public double ContentWidth { get => (double)GetValue(ContentWidthProperty); set => SetValue(ContentWidthProperty, value); }
    public static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(PianoRollView), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public double HorizontalOffset { get => (double)GetValue(HorizontalOffsetProperty); set => SetValue(HorizontalOffsetProperty, value); }
    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(nameof(ViewportWidth), typeof(double), typeof(PianoRollView), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.Register(nameof(VerticalOffset), typeof(double), typeof(PianoRollView), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public double VerticalOffset { get => (double)GetValue(VerticalOffsetProperty); set => SetValue(VerticalOffsetProperty, value); }
    public static readonly DependencyProperty KeyColumnWidthProperty = DependencyProperty.Register(nameof(KeyColumnWidth), typeof(double), typeof(PianoRollView), new FrameworkPropertyMetadata(72d, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public double KeyColumnWidth { get => (double)GetValue(KeyColumnWidthProperty); set => SetValue(KeyColumnWidthProperty, value); }
    public static readonly DependencyProperty LaneHeightProperty = DependencyProperty.Register(nameof(LaneHeight), typeof(double), typeof(PianoRollView), new FrameworkPropertyMetadata(36d, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public double LaneHeight { get => (double)GetValue(LaneHeightProperty); set => SetValue(LaneHeightProperty, value); }
    public static readonly DependencyProperty ZoomFactorProperty = DependencyProperty.Register(nameof(ZoomFactor), typeof(double), typeof(PianoRollView), new FrameworkPropertyMetadata(32d, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public double ZoomFactor { get => (double)GetValue(ZoomFactorProperty); set => SetValue(ZoomFactorProperty, value); }
    public static readonly DependencyProperty CanEditProperty = DependencyProperty.Register(nameof(CanEdit), typeof(bool), typeof(PianoRollView), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public bool CanEdit { get => (bool)GetValue(CanEditProperty); set => SetValue(CanEditProperty, value); }
    public static readonly DependencyProperty HoverBrushProperty = DependencyProperty.Register(nameof(HoverBrush), typeof(Brush), typeof(PianoRollView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? HoverBrush { get => (Brush?)GetValue(HoverBrushProperty); set => SetValue(HoverBrushProperty, value); }
    public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(nameof(MarkerBrush), typeof(Brush), typeof(PianoRollView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? MarkerBrush { get => (Brush?)GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }
    public static readonly DependencyProperty PressedBrushProperty = DependencyProperty.Register(nameof(PressedBrush), typeof(Brush), typeof(PianoRollView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? PressedBrush { get => (Brush?)GetValue(PressedBrushProperty); set => SetValue(PressedBrushProperty, value); }
    public static readonly DependencyProperty SelectionBrushProperty = DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(PianoRollView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? SelectionBrush { get => (Brush?)GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }
    public static readonly DependencyProperty SelectionCueBrushProperty = DependencyProperty.Register(nameof(SelectionCueBrush), typeof(Brush), typeof(PianoRollView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? SelectionCueBrush { get => (Brush?)GetValue(SelectionCueBrushProperty); set => SetValue(SelectionCueBrushProperty, value); }
    public static readonly DependencyProperty FocusBrushProperty = DependencyProperty.Register(nameof(FocusBrush), typeof(Brush), typeof(PianoRollView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? FocusBrush { get => (Brush?)GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }
    public static readonly DependencyProperty PlaybackMarkerBrushProperty = DependencyProperty.Register(nameof(PlaybackMarkerBrush), typeof(Brush), typeof(PianoRollView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? PlaybackMarkerBrush { get => (Brush?)GetValue(PlaybackMarkerBrushProperty); set => SetValue(PlaybackMarkerBrushProperty, value); }
    public static readonly DependencyProperty NodeBorderBrushProperty = DependencyProperty.Register(nameof(NodeBorderBrush), typeof(Brush), typeof(PianoRollView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? NodeBorderBrush { get => (Brush?)GetValue(NodeBorderBrushProperty); set => SetValue(NodeBorderBrushProperty, value); }
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(nameof(GridBrush), typeof(Brush), typeof(PianoRollView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? GridBrush { get => (Brush?)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public static readonly DependencyProperty KeyBrushProperty = DependencyProperty.Register(nameof(KeyBrush), typeof(Brush), typeof(PianoRollView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? KeyBrush { get => (Brush?)GetValue(KeyBrushProperty); set => SetValue(KeyBrushProperty, value); }
    public static readonly DependencyProperty SharpKeyBrushProperty = DependencyProperty.Register(nameof(SharpKeyBrush), typeof(Brush), typeof(PianoRollView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? SharpKeyBrush { get => (Brush?)GetValue(SharpKeyBrushProperty); set => SetValue(SharpKeyBrushProperty, value); }
    public static readonly RoutedEvent NoteEditRequestedEvent = EventManager.RegisterRoutedEvent(nameof(NoteEditRequested), RoutingStrategy.Bubble, typeof(EventHandler<InstrumentalNoteEditRequestedEventArgs>), typeof(PianoRollView));
    public event EventHandler<InstrumentalNoteEditRequestedEventArgs> NoteEditRequested { add => AddHandler(NoteEditRequestedEvent, value); remove => RemoveHandler(NoteEditRequestedEvent, value); }
    private int activeKey = 36;
    private double activeMs, grabOffset;
    private Point pointerStart;
    private InstrumentalNoteSnapshot? pressedNote;
    private (string Key, double Time)? hover;
    private bool dragging, pointerInteraction;
    private static readonly DependencyPropertyKey MaximumVerticalOffsetPropertyKey = DependencyProperty.RegisterReadOnly(nameof(MaximumVerticalOffset), typeof(double), typeof(PianoRollView), new FrameworkPropertyMetadata(0d));
    public static readonly DependencyProperty MaximumVerticalOffsetProperty = MaximumVerticalOffsetPropertyKey.DependencyProperty;
    public double MaximumVerticalOffset => (double)GetValue(MaximumVerticalOffsetProperty);
    public string ActiveCell => string.Format(CultureInfo.CurrentCulture, "{0}, {1:0.###} ms", InstrumentalKeys.GetName(activeKey), activeMs);
    public PianoRollView()
    {
        Focusable = true; FlowDirection = FlowDirection.LeftToRight;
        Loaded += (_, _) => { SystemParameters.StaticPropertyChanged -= OnSystemChanged; SystemParameters.StaticPropertyChanged += OnSystemChanged; };
        Unloaded += (_, _) => { SystemParameters.StaticPropertyChanged -= OnSystemChanged; CancelInteraction(); };
    }
    private void OnSystemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (Dispatcher.CheckAccess()) InvalidateVisual(); else Dispatcher.BeginInvoke(InvalidateVisual); }
    private static void OnGeometryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    { var roll = (PianoRollView)d; if (e.Property == LaneHeightProperty || e.Property == VerticalOffsetProperty) roll.OnMaximumChanged(); roll.InvalidateVisual(); }
    protected override void OnRenderSizeChanged(SizeChangedInfo info) { base.OnRenderSizeChanged(info); OnMaximumChanged(); }
    private void OnMaximumChanged()
    { SetValue(MaximumVerticalOffsetPropertyKey, double.IsFinite(LaneHeight) ? Math.Max(0, 108 * LaneHeight - ActualHeight) : 0d); var value = Math.Clamp(VerticalOffset, 0, MaximumVerticalOffset); if (value != VerticalOffset) SetCurrentValue(VerticalOffsetProperty, value); }
    public Rect GetNoteBounds(InstrumentalNoteSnapshot note)
    {
        if (!InstrumentalKeys.TryGetIndex(note.Key, out var index) || !double.IsFinite(LaneHeight) || LaneHeight <= 0) return Rect.Empty;
        var bounds = OHsSampleNodeDrawing.GetBounds(note.Timestamp, Duration, ContentWidth, HorizontalOffset, (107 - index) * LaneHeight - VerticalOffset, LaneHeight, ZoomFactor);
        if (!bounds.IsEmpty) bounds.Offset(KeyColumnWidth, 0); return bounds;
    }
    public InstrumentalNoteSnapshot? FindNoteAt(Point p)
    {
        if (p.X < KeyColumnWidth || p.X >= ActualWidth || p.Y < 0 || p.Y >= ActualHeight) return null;
        return Notes.Reverse().OrderByDescending(n => n.Id == SelectedNoteId).FirstOrDefault(n => GetNoteBounds(n).Contains(p));
    }
    private bool CellAt(Point p, out string key, out double ms)
    {
        key = ""; ms = 0;
        if (!CanEdit || LaneHeight <= 0 || !double.IsFinite(LaneHeight) || ContentWidth <= 0 || Duration <= TimeSpan.Zero || p.X < KeyColumnWidth || p.X >= ActualWidth || p.Y < 0 || p.Y >= ActualHeight) return false;
        var index = 107 - (int)Math.Floor((p.Y + VerticalOffset) / LaneHeight);
        if (index < 0 || index > 107) return false;
        key = InstrumentalKeys.GetName(index); ms = (p.X - KeyColumnWidth + HorizontalOffset) / ContentWidth * Duration.TotalMilliseconds;
        return double.IsFinite(ms) && ms >= 0 && ms < Duration.TotalMilliseconds;
    }
    private double GridTime(double ms)
    {
        if (Bpms.Count == 0 || Bpms.Any(b => b <= 0)) return ms;
        var radius = 60000d / Bpms.Min() / Math.Max(1, Subdivision);
        var nearest = BeatGridCalculator.GetLines(Offsets, Bpms, Subdivision, Math.Max(0, ms - radius), Math.Min(Duration.TotalMilliseconds, ms + radius))
         .OrderBy(l => Math.Abs(l.Milliseconds - ms)).FirstOrDefault();
        return Math.Abs(nearest.Milliseconds - ms) <= .500001 ? nearest.Milliseconds : ms;
    }
    private void Request(InstrumentalNoteEditKind kind, Guid id, string key, double ms) => RaiseEvent(new InstrumentalNoteEditRequestedEventArgs(NoteEditRequestedEvent, kind, id, key, ms));
    public bool HandleEditorKey(Key key, ModifierKeys modifiers, bool repeat)
    {
        if (!CanEdit) return false;
        if (repeat && (key is Key.Enter or Key.Delete || modifiers == ModifierKeys.Alt)) return true;
        var selected = Notes.FirstOrDefault(n => n.Id == SelectedNoteId);
        if (modifiers == ModifierKeys.Alt && key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            if (selected is null) return true;
            InstrumentalKeys.TryGetIndex(selected.Key, out var pitch);
            if (key is Key.Up or Key.Down)
            { pitch += key == Key.Up ? 1 : -1; if (pitch is >= 0 and < 108) Request(InstrumentalNoteEditKind.Move, selected.Id, InstrumentalKeys.GetName(pitch), selected.Timestamp); }
            else if (BeatGridCalculator.GetAdjacentPosition(Offsets, Bpms, Subdivision, TimeSpan.FromMilliseconds(GridTime(selected.Timestamp)), Duration, key == Key.Right) is { } next)
                Request(InstrumentalNoteEditKind.Move, selected.Id, selected.Key, next.TotalMilliseconds);
            return true;
        }
        if (modifiers != ModifierKeys.None) return false;
        if (key == Key.Enter) { Request(InstrumentalNoteEditKind.Place, Guid.Empty, InstrumentalKeys.GetName(activeKey), activeMs); return true; }
        if (key == Key.Delete) { if (selected is not null) Request(InstrumentalNoteEditKind.Remove, selected.Id, selected.Key, selected.Timestamp); return true; }
        if (key == Key.Up) activeKey = Math.Min(107, activeKey + 1);
        else if (key == Key.Down) activeKey = Math.Max(0, activeKey - 1);
        else if (key is Key.Left or Key.Right)
        { if (BeatGridCalculator.GetAdjacentPosition(Offsets, Bpms, Subdivision, TimeSpan.FromMilliseconds(activeMs), Duration, key == Key.Right) is { } next) activeMs = next.TotalMilliseconds; }
        else return false;
        RevealActive(); InvalidateVisual(); return true;
    }
    private void RevealActive()
    {
        var top = (107 - activeKey) * LaneHeight;
        if (top < VerticalOffset) SetCurrentValue(VerticalOffsetProperty, top);
        else if (top + LaneHeight > VerticalOffset + ActualHeight) SetCurrentValue(VerticalOffsetProperty, Math.Clamp(top + LaneHeight - ActualHeight, 0, MaximumVerticalOffset));
        if (Duration.TotalMilliseconds <= 0 || ContentWidth <= 0) return;
        var x = activeMs / Duration.TotalMilliseconds * ContentWidth;
        var width = Math.Max(1, ActualWidth - KeyColumnWidth);
        if (x < HorizontalOffset || x > HorizontalOffset + width) SetCurrentValue(HorizontalOffsetProperty, Math.Clamp(x - width / 2, 0, Math.Max(0, ContentWidth - width)));
    }
    public bool CancelInteraction()
    { var active = pointerInteraction || IsMouseCaptured; pointerInteraction = false; pressedNote = null; dragging = false; hover = null; if (IsMouseCaptured) ReleaseMouseCapture(); InvalidateVisual(); return active; }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    { base.OnGotKeyboardFocus(e); activeMs = BeatGridCalculator.GetContainingInterval(Offsets, Bpms, Subdivision, Position.TotalMilliseconds, Duration.TotalMilliseconds)?.StartMilliseconds ?? 0; RevealActive(); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e) { if (HandleEditorKey(e.Key, Keyboard.Modifiers, e.IsRepeat)) { e.Handled = true; return; } base.OnKeyDown(e); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); if (!CanEdit) return; Focus();
        if (!BeginPointerInteraction(e.GetPosition(this))) return;
        if (!CaptureMouse()) CancelInteraction();
        e.Handled = true;
    }
    internal bool BeginPointerInteraction(Point p)
    {
        if (!CellAt(p, out var key, out var ms)) return false;
        pointerInteraction = true;
        pointerStart = p; pressedNote = FindNoteAt(p); dragging = false; hover = (key, ms);
        if (pressedNote is not null) { SetCurrentValue(SelectedNoteIdProperty, pressedNote.Id); InstrumentalKeys.TryGetIndex(pressedNote.Key, out activeKey); activeMs = GridTime(pressedNote.Timestamp); grabOffset = ms - pressedNote.Timestamp; }
        else { SetCurrentValue(SelectedNoteIdProperty, null); InstrumentalKeys.TryGetIndex(key, out activeKey); activeMs = BeatGridCalculator.GetContainingInterval(Offsets, Bpms, Subdivision, ms, Duration.TotalMilliseconds)?.StartMilliseconds ?? ms; }
        InvalidateVisual(); return true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); UpdatePointerPosition(e.GetPosition(this));
    }
    internal void UpdatePointerPosition(Point p)
    {
        if (CellAt(p, out var key, out var ms))
        {
            if (pointerInteraction && pressedNote is not null && (Math.Abs(p.X - pointerStart.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y - pointerStart.Y) >= SystemParameters.MinimumVerticalDragDistance)) dragging = true;
            hover = (key, dragging ? ms - grabOffset : ms);
        }
        else hover = null;
        InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (CompletePointerInteraction()) e.Handled = true;
    }
    internal bool CompletePointerInteraction()
    {
        if (!pointerInteraction) return false;
        var note = pressedNote; var moved = dragging; var target = hover; CancelInteraction();
        if (CanEdit && target is { } cell)
        {
            if (note is not null && moved) Request(InstrumentalNoteEditKind.Move, note.Id, cell.Key, cell.Time);
            else if (note is null && !moved) Request(InstrumentalNoteEditKind.Place, Guid.Empty, cell.Key, cell.Time);
        }
        return true;
    }
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    { base.OnMouseRightButtonDown(e); if (!CanEdit) return; var note = FindNoteAt(e.GetPosition(this)); if (note is null) return; Request(InstrumentalNoteEditKind.Remove, note.Id, note.Key, note.Timestamp); e.Handled = true; }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); CancelInteraction(); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); if (!IsMouseCaptured) { hover = null; InvalidateVisual(); } }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    { if (Keyboard.Modifiers != ModifierKeys.None) { base.OnMouseWheel(e); return; } SetCurrentValue(VerticalOffsetProperty, Math.Clamp(VerticalOffset - Math.Sign(e.Delta) * LaneHeight * 3, 0, MaximumVerticalOffset)); e.Handled = true; }
    protected override AutomationPeer OnCreateAutomationPeer() => new PianoRollViewAutomationPeer(this);
    private Brush BrushOr(Brush? value, string key) => value ?? TryFindResource(key) as Brush ?? SystemColors.WindowTextBrush;
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); dc.DrawRectangle(SystemParameters.HighContrast ? SystemColors.WindowBrush : BrushOr(Background, "Brush.Background.Deepened"), null, new Rect(RenderSize));
        if (ActualWidth <= KeyColumnWidth || ActualHeight <= 0 || LaneHeight <= 0 || !double.IsFinite(LaneHeight)) return;
        var hc = SystemParameters.HighContrast; var text = hc ? SystemColors.WindowTextBrush : BrushOr(Foreground, "Brush.Text.Primary");
        var marker = hc ? SystemColors.WindowTextBrush : BrushOr(MarkerBrush, "Brush.Navy.Muted");
        var selection = hc ? SystemColors.HighlightBrush : BrushOr(SelectionBrush, "Brush.Navy");
        var cue = hc ? SystemColors.HighlightTextBrush : BrushOr(SelectionCueBrush, "Brush.Text.Primary");
        var border = new Pen(hc ? SystemColors.WindowTextBrush : BrushOr(NodeBorderBrush, "Brush.Border.Default"), 1);
        var first = Math.Max(0, (int)Math.Floor(VerticalOffset / LaneHeight)); var last = Math.Min(107, (int)Math.Ceiling((VerticalOffset + ActualHeight) / LaneHeight));
        for (var row = first; row <= last; row++)
        {
            var name = InstrumentalKeys.GetName(107 - row); var y = row * LaneHeight - VerticalOffset;
            dc.DrawRectangle(hc ? SystemColors.WindowBrush : BrushOr(name.Contains('#') ? SharpKeyBrush : KeyBrush, name.Contains('#') ? "Brush.Background.Deepened" : "Brush.Background.Surface"), border, new Rect(0, y, KeyColumnWidth, LaneHeight));
            var label = new FormattedText(name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), 11, text, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new Point(Math.Max(4, KeyColumnWidth - label.Width - 8), y + (LaneHeight - label.Height) / 2));
            dc.DrawLine(new Pen(hc ? SystemColors.WindowTextBrush : BrushOr(GridBrush, "Brush.Border.Subtle"), .5), new(KeyColumnWidth, y + LaneHeight), new(ActualWidth, y + LaneHeight));
            if (IsKeyboardFocused && 107 - row == activeKey) dc.DrawLine(new Pen(text, 2), new(8, y + LaneHeight - 3), new(KeyColumnWidth - 8, y + LaneHeight - 3));
        }
        dc.PushClip(new RectangleGeometry(new Rect(KeyColumnWidth, 0, Math.Max(0, ActualWidth - KeyColumnWidth), ActualHeight)));
        dc.PushTransform(new TranslateTransform(KeyColumnWidth, 0)); dc.PushOpacity(.5);
        BeatGridDrawing.Draw(dc, this, Offsets, Bpms, Subdivision, Duration.TotalMilliseconds, ContentWidth, HorizontalOffset, Math.Max(0, ActualWidth - KeyColumnWidth), ActualHeight, false);
        dc.Pop(); dc.Pop();
        if (hover is { } cell)
        {
            var ts = dragging ? BeatGridCalculator.GetNearestTimestamp(Offsets, Bpms, Subdivision, cell.Time, Duration.TotalMilliseconds) : BeatGridCalculator.GetContainingInterval(Offsets, Bpms, Subdivision, cell.Time, Duration.TotalMilliseconds) is { } interval ? (int?)Math.Round(interval.StartMilliseconds, MidpointRounding.AwayFromZero) : null;
            if (ts is { } t) { var rect = GetNoteBounds(new(Guid.Empty, cell.Key, t, 100)); if (!rect.IsEmpty) OHsSampleNodeDrawing.Draw(dc, hc ? SystemColors.HighlightBrush : BrushOr(HoverBrush, "Brush.Background.Hover"), null, OHsSampleNodeDrawing.Inset(rect)); }
        }
        foreach (var note in Notes.OrderBy(n => n.Id == SelectedNoteId))
        {
            var rect = GetNoteBounds(note); if (rect.IsEmpty || !rect.IntersectsWith(new Rect(KeyColumnWidth, 0, Math.Max(0, ActualWidth - KeyColumnWidth), ActualHeight)) || dragging && note == pressedNote) continue;
            var chosen = note.Id == SelectedNoteId;
            OHsSampleNodeDrawing.Draw(dc, chosen ? selection : marker, chosen ? new Pen(cue, 1) : border, OHsSampleNodeDrawing.Inset(rect));
            if (note == pressedNote) OHsSampleNodeDrawing.Draw(dc, null, new Pen(hc ? SystemColors.HighlightTextBrush : BrushOr(PressedBrush, "Brush.Navy"), 2), OHsSampleNodeDrawing.Inset(rect));
        }
        if (IsKeyboardFocused)
        {
            var t = (int)Math.Round(activeMs, MidpointRounding.AwayFromZero); var r = GetNoteBounds(new(Guid.Empty, InstrumentalKeys.GetName(activeKey), t, 100));
            if (!r.IsEmpty) dc.DrawLine(new Pen(text, 2), new(r.Left, r.Bottom + 2), new(r.Right, r.Bottom + 2));
        }
        if (Duration > TimeSpan.Zero && ContentWidth > 0) { var x = KeyColumnWidth + Position.TotalMilliseconds / Duration.TotalMilliseconds * ContentWidth - HorizontalOffset; dc.DrawLine(new Pen(hc ? SystemColors.WindowTextBrush : BrushOr(PlaybackMarkerBrush, "Brush.Text.Primary"), 1.5), new(x, 0), new(x, ActualHeight)); }
        dc.Pop();
    }
}
