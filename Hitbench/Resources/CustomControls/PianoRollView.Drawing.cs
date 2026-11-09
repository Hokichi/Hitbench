using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using Hitbench.Core.Constants;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Resources.CustomControls;

public sealed partial class PianoRollView
{
    private readonly DrawingVisual contentVisual = new();
    private readonly DrawingVisual markerVisual = new();
    private readonly TranslateTransform markerTransform = new();
    private readonly RectangleGeometry markerClip = new();
    private bool contentDirty = true;
    internal long ContentBuildCount { get; private set; }
    protected override int VisualChildrenCount => base.VisualChildrenCount + 2;
    protected override Visual GetVisualChild(int index) => index < base.VisualChildrenCount ? base.GetVisualChild(index)
        : (index - base.VisualChildrenCount) switch
        { 0 => contentVisual, 1 => markerVisual, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    protected override Size MeasureOverride(Size constraint) => base.VisualChildrenCount > 0 ? base.MeasureOverride(constraint) : new Size();
    protected override Size ArrangeOverride(Size bounds) => base.VisualChildrenCount > 0 ? base.ArrangeOverride(bounds) : bounds;

    private void InitializeDrawing()
    {
        AddVisualChild(contentVisual); AddVisualChild(markerVisual);
        markerVisual.Transform = markerTransform; markerVisual.Clip = markerClip;
    }
    private void InvalidateContent() { contentDirty = true; InvalidateVisual(); }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != PositionProperty && (e.Property == IsEnabledProperty ||
            e.Property.GetMetadata(GetType()) is FrameworkPropertyMetadata metadata && (metadata.AffectsRender || metadata.AffectsMeasure))) InvalidateContent();
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    { base.OnDpiChanged(oldDpi, newDpi); InvalidateContent(); }
    internal void PrepareDrawing()
    {
        if (contentDirty)
        {
            using (var dc = contentVisual.RenderOpen()) DrawContent(dc);
            using (var dc = markerVisual.RenderOpen())
                dc.DrawLine(new Pen(SystemParameters.HighContrast ? SystemColors.WindowTextBrush : BrushOr(PlaybackMarkerBrush, "Brush.Playhead"), 1.5), new(0, 0), new(0, ActualHeight));
            contentDirty = false; ContentBuildCount++;
        }
        UpdateMarker();
    }
    protected override void OnRender(DrawingContext dc) { base.OnRender(dc); PrepareDrawing(); }
    internal double? PlaybackMarkerX => Duration > TimeSpan.Zero && ContentWidth > 0 && TimeViewportWidth > 0
        ? KeyColumnWidth + (IsPlaybackMarkerStatic ? TimeViewportWidth / 2 : Position.TotalMilliseconds / Duration.TotalMilliseconds * ContentWidth - HorizontalOffset) : null;
    private void UpdateMarker()
    {
        var x = PlaybackMarkerX ?? double.NaN;
        markerVisual.Opacity = double.IsFinite(x) && TimeViewportWidth > 0 && ActualHeight > 0 ? 1 : 0;
        markerTransform.X = double.IsFinite(x) ? x : 0;
        markerClip.Rect = new Rect(KeyColumnWidth - markerTransform.X, 0, TimeViewportWidth, Math.Max(0, ActualHeight));
    }
    private Brush BrushOr(Brush? value, string key) => value ?? TryFindResource(key) as Brush ?? SystemColors.WindowTextBrush;
    private void DrawContent(DrawingContext dc)
    {
        dc.DrawRectangle(SystemParameters.HighContrast ? SystemColors.WindowBrush : BrushOr(Background, "Brush.Background.Deepened"), null, new Rect(RenderSize));
        if (ActualWidth <= KeyColumnWidth || ActualHeight <= 0 || LaneHeight <= 0 || !double.IsFinite(LaneHeight)) return;
        var hc = SystemParameters.HighContrast; var text = hc ? SystemColors.WindowTextBrush : BrushOr(Foreground, "Brush.Text.Primary");
        var marker = hc ? SystemColors.WindowTextBrush : BrushOr(MarkerBrush, Hitbench.Resources.Helpers.OHsTrackPalette.DefaultResourceKey);
        var selection = hc ? SystemColors.HighlightBrush : BrushOr(SelectionBrush, "Brush.Navy");
        var cue = hc ? SystemColors.HighlightTextBrush : BrushOr(SelectionCueBrush, "Brush.Node.SelectedOutline");
        var border = new Pen(hc ? SystemColors.WindowTextBrush : BrushOr(NodeBorderBrush, "Brush.Border.Default"), 1);
        var first = Math.Max(0, (int)Math.Floor(VerticalOffset / LaneHeight)); var last = Math.Min(107, (int)Math.Ceiling((VerticalOffset + ActualHeight) / LaneHeight));
        for (var row = first; row <= last; row++)
        {
            var name = InstrumentalKeys.GetName(107 - row); var y = row * LaneHeight - VerticalOffset;
            dc.DrawRectangle(hc ? SystemColors.WindowBrush : BrushOr(name.Contains('#') ? SharpKeyBrush : KeyBrush, name.Contains('#') ? "Brush.Background.Deepened" : "Brush.Background.Surface"), border, new Rect(0, y, KeyColumnWidth, LaneHeight));
            var label = new FormattedText(name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), 11, text, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new Point(Math.Max(4, KeyColumnWidth - label.Width - 8), y + (LaneHeight - label.Height) / 2));
            dc.DrawLine(new Pen(hc ? SystemColors.WindowTextBrush : BrushOr(GridBrush, "Brush.Border.Subtle"), .5), new(KeyColumnWidth, y + LaneHeight), new(KeyColumnWidth + TimeViewportWidth, y + LaneHeight));
        }
        dc.PushClip(new RectangleGeometry(new Rect(KeyColumnWidth, 0, TimeViewportWidth, ActualHeight)));
        dc.PushTransform(new TranslateTransform(KeyColumnWidth, 0)); dc.PushOpacity(.5);
        BeatGridDrawing.Draw(dc, this, Offsets, Bpms, Subdivision, Duration.TotalMilliseconds, ContentWidth, HorizontalOffset, TimeViewportWidth, ActualHeight, false);
        dc.Pop(); dc.Pop();
        if(pointerInteraction && dragging && !IsAdditionMode && !pointerModifiers.HasFlag(ModifierKeys.Control) && pressedNote is null)
            dc.DrawRectangle(null,new Pen(selection,1),SelectionViewportBounds);
        if(dragging && pressedNote is {} anchor && hover is {} destination && !IsAdditionMode)
        {
            InstrumentalKeys.TryGetIndex(anchor.Key,out var originalPitch);InstrumentalKeys.TryGetIndex(destination.Key,out var targetPitch);
            var time=BeatGridCalculator.GetNearestTimestamp(Offsets,Bpms,Subdivision,destination.Time,Duration.TotalMilliseconds);
            if(time is {} timestamp)foreach(var note in Notes.Where(n=>gestureIds.Contains(n.Id)))
            {
                InstrumentalKeys.TryGetIndex(note.Key,out var pitch);pitch+=targetPitch-originalPitch;
                var movedTime=(long)note.Timestamp+timestamp-anchor.Timestamp;
                if(pitch is <0 or >107 || movedTime<0 || movedTime>=Duration.TotalMilliseconds || movedTime>int.MaxValue)continue;
                var bounds=GetNoteBounds(note with{Key=InstrumentalKeys.GetName(pitch),Timestamp=(int)movedTime});
                if(!bounds.IsEmpty)OHsSampleNodeDrawing.Draw(dc,marker,new Pen(cue,2),OHsSampleNodeDrawing.Inset(bounds));
            }
        }
        if (IsAdditionMode && hover is { } cell)
        {
            var ts = dragging ? BeatGridCalculator.GetNearestTimestamp(Offsets, Bpms, Subdivision, cell.Time, Duration.TotalMilliseconds) : BeatGridCalculator.GetContainingInterval(Offsets, Bpms, Subdivision, cell.Time, Duration.TotalMilliseconds) is { } interval ? (int?)Math.Round(interval.StartMilliseconds, MidpointRounding.AwayFromZero) : null;
            if (ts is { } t) { var rect = GetNoteBounds(new(Guid.Empty, cell.Key, t)); if (!rect.IsEmpty) OHsSampleNodeDrawing.Draw(dc, marker, new Pen(cue, 2), OHsSampleNodeDrawing.Inset(rect)); }
        }
        foreach (var note in Notes.OrderBy(n => n.Id == SelectedNoteId))
        {
            var rect = GetNoteBounds(note); if (rect.IsEmpty || !rect.IntersectsWith(new Rect(KeyColumnWidth, 0, TimeViewportWidth, ActualHeight)) || dragging && !IsAdditionMode && gestureIds.Contains(note.Id)) continue;
            var chosen = SelectedNoteIds.Contains(note.Id) || note.Id == SelectedNoteId;
            OHsSampleNodeDrawing.Draw(dc, marker, chosen ? new Pen(cue, 2) : border, OHsSampleNodeDrawing.Inset(rect));
            if (note == pressedNote) OHsSampleNodeDrawing.Draw(dc, null, new Pen(chosen ? cue : hc ? SystemColors.HighlightTextBrush : BrushOr(PressedBrush, "Brush.Navy.Pressed"), 2), OHsSampleNodeDrawing.Inset(rect));
        }
        if (IsKeyboardFocused)
        {
            var t = (int)Math.Round(activeMs, MidpointRounding.AwayFromZero); var r = GetNoteBounds(new(Guid.Empty, InstrumentalKeys.GetName(activeKey), t));
            if (!r.IsEmpty) dc.DrawEllipse(text,null,new Point(r.Left+4,r.Top+4),2,2);
        }
        dc.Pop();
    }
}
