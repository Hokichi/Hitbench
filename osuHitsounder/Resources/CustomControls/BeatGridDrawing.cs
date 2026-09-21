using System.Windows;
using System.Windows.Media;
using osuHitsounder.Services;

namespace osuHitsounder.Resources.CustomControls;

public static class BeatGridDrawing
{
    public static void Draw(DrawingContext context, FrameworkElement owner, IReadOnlyList<int>? offsets,
        IReadOnlyList<int>? bpms, int subdivision, double durationMs, double contentWidth,
        double horizontalOffset, double viewportWidth, double height, bool isWaveform)
    {
        if (offsets is null || bpms is null || durationMs <= 0 || contentWidth <= 0 || viewportWidth <= 0 || height <= 0) return;
        var startMs = horizontalOffset / contentWidth * durationMs;
        var endMs = (horizontalOffset + viewportWidth) / contentWidth * durationMs;
        var highContrast = SystemParameters.HighContrast;
        foreach (var line in BeatGridCalculator.GetLines(offsets, bpms, subdivision, startMs, endMs))
        {
            var key = isWaveform ? line.Division switch
            {
                1 => "Brush.Beat.Whole",
                2 => "Brush.Beat.Half",
                4 => "Brush.Beat.Fourth",
                3 or 6 or 12 => "Brush.Beat.Triplet",
                5 or 8 => "Brush.Beat.FifthEighth",
                _ => "Brush.Beat.Sixteenth"
            } : line.Division == 1 ? "Brush.Beat.Whole" : "Brush.Beat.Sixteenth";
            var brush = highContrast ? SystemColors.WindowTextBrush : owner.TryFindResource(key) as Brush ?? Brushes.White;
            var x = line.Milliseconds / durationMs * contentWidth - (isWaveform ? 0 : horizontalOffset);
            var barHeight = isWaveform ? Math.Min(height, line.Division == 1 ? 50 : 35) : height;
            context.DrawLine(new Pen(brush, line.Division == 1 ? 1 : 0.5), new Point(x, 0), new Point(x, barHeight));
        }
    }
}
