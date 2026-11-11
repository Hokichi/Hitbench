using Hitbench.Core.Entities;
using System.Windows;
using System.Windows.Media;

namespace Hitbench.Resources.CustomControls;

public sealed partial class BeatGridView
{
    private void DrawContent(DrawingContext context)
    {
        context.PushClip(new RectangleGeometry(new Rect(retainedViewport.Start, 0, retainedViewport.Width, ActualHeight)));
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var highContrast = SystemParameters.HighContrast;
        var pressed = highContrast ? SystemColors.HighlightTextBrush : PressedBrush;
        var marker = highContrast ? SystemColors.WindowTextBrush : MarkerBrush;
        var selectionCue = highContrast ? SystemColors.HighlightBrush : SelectionCueBrush ?? TryFindResource("Brush.Node.SelectedOutline") as Brush ?? SystemColors.WindowTextBrush;
        var nodeBorder = new Pen(highContrast ? SystemColors.WindowTextBrush :
            TryFindResource("Brush.Border.Default") as Brush ?? marker, 1);
        var selectedBorder = new Pen(selectionCue, 2);
        context.PushOpacity(highContrast ? 1 : 0.5);
        context.PushTransform(new TranslateTransform(retainedViewport.Start, 0));
        BeatGridDrawing.Draw(context, this, UninheritedPoints, Subdivision, Duration.TotalMilliseconds,
            ContentWidth, retainedViewport.Origin + retainedViewport.Start, retainedViewport.Width, ActualHeight, false);
        context.Pop();
        context.Pop();
        var left = retainedViewport.Origin + retainedViewport.Start - HorizontalOffset;
        var visible = SamplesInRange(left, left + retainedViewport.Width);
        for (var layer = 0; layer < 2; layer++)
        {
            var selectedLayer = layer == 1;
            if (marker is not null && Samples is not null)
                foreach (var sample in visible)
                {
                    if (IsSampleSelected(sample) != selectedLayer) continue;
                    var rect = RetainedBounds(GetDisplayedSampleBounds(sample));
                    if (rect.IsEmpty) continue;
                    rect = Inset(rect);
                    OHsSampleNodeDrawing.Draw(context,marker, selectedLayer ? selectedBorder : nodeBorder, rect);
                }
            if (Samples is not null)
                foreach (var sample in visible)
                {
                    if (IsSampleSelected(sample) != selectedLayer) continue;
                    var bounds = RetainedBounds(GetDisplayedSampleBounds(sample));
                    if (bounds.IsEmpty) continue;
                    var rect = Inset(bounds);
                    if (ReferenceEquals(PressedSample, sample) && pressed is not null)
                        OHsSampleNodeDrawing.Draw(context,null, new Pen(pressed, 2), rect);
                    if (selectedLayer && selectionCue is not null)
                        OHsSampleNodeDrawing.Draw(context,null, selectedBorder, rect);
                }
        }
        if (DragPreviewTimestamps is { } previews)
            foreach (var preview in previews)
            {
                var bounds = RetainedBounds(GetSampleBounds(preview));
                if (!bounds.IsEmpty)
                {
                    var rect = Inset(bounds);
                    context.PushOpacity(TryFindResource("Opacity.Disabled") is double opacity ? opacity : 0.4);
                    OHsSampleNodeDrawing.Draw(context, marker, null, rect);
                    context.Pop();
                    OHsSampleNodeDrawing.Draw(context, null, selectedBorder, rect);
                }
            }
        context.Pop();
    }
    private void DrawFeedback(DrawingContext context)
    {
        context.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        var highContrast = SystemParameters.HighContrast;
        var pressed = highContrast ? SystemColors.HighlightTextBrush : PressedBrush;
        var marker = highContrast ? SystemColors.WindowTextBrush : MarkerBrush;
        var selectionCue = highContrast ? SystemColors.HighlightBrush : SelectionCueBrush ?? TryFindResource("Brush.Node.SelectedOutline") as Brush ?? SystemColors.WindowTextBrush;
        var selectedBorder = new Pen(selectionCue, 2);
        if (HasPlacementPreview && ActiveInterval is { } active)
        {
            var rect = Inset(GetSampleBounds((int)Math.Round(active.StartMilliseconds, MidpointRounding.AwayFromZero)));
            context.PushOpacity(highContrast ? 1 : 0.4);
            OHsSampleNodeDrawing.Draw(context, marker, null, rect);
            context.Pop();
            // Back the outline so overlapping nodes and custom colors cannot erase the preview cue.
            var previewBacking = highContrast ? SystemColors.WindowBrush :
                TryFindResource("Brush.Background.Surface") as Brush ?? SystemColors.WindowBrush;
            var previewBorder = new Pen(highContrast ? SystemColors.WindowTextBrush :
                TryFindResource("Brush.Text.Primary") as Brush ?? SystemColors.WindowTextBrush, 1) { DashStyle = DashStyles.Dash };
            OHsSampleNodeDrawing.Draw(context, null, new Pen(previewBacking, 3), rect);
            OHsSampleNodeDrawing.Draw(context, null, previewBorder, rect);
            if (pressedInterval is not null && pressed is not null)
                OHsSampleNodeDrawing.Draw(context, null, new Pen(pressed, 1), rect);
        }
        context.Pop();
    }
}
