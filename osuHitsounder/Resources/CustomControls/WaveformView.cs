using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Core.Entities;

namespace osuHitsounder.Resources.CustomControls;

public sealed class WaveformView : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(WaveformData), typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(TimeSpan), typeof(WaveformView), new FrameworkPropertyMetadata(TimeSpan.Zero, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ZoomFactorProperty = DependencyProperty.Register(
        nameof(ZoomFactor), typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(
        nameof(ViewportWidth), typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.Register(
        nameof(HorizontalOffset), typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty WaveformBrushProperty = DependencyProperty.Register(
        nameof(WaveformBrush), typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(SystemColors.HighlightBrush, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(
        nameof(MarkerBrush), typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(SystemColors.HighlightTextBrush, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(SystemColors.WindowTextBrush, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
        nameof(FontFamily), typeof(FontFamily), typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(WaveformView), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty OffsetsProperty = DependencyProperty.Register(nameof(Offsets), typeof(IReadOnlyList<int>), typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BpmsProperty = DependencyProperty.Register(nameof(Bpms), typeof(IReadOnlyList<int>), typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SubdivisionProperty = DependencyProperty.Register(nameof(Subdivision), typeof(int), typeof(WaveformView), new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsRender));

    public WaveformData? Data { get => (WaveformData?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public TimeSpan Position { get => (TimeSpan)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public double ZoomFactor { get => (double)GetValue(ZoomFactorProperty); set => SetValue(ZoomFactorProperty, value); }
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double HorizontalOffset { get => (double)GetValue(HorizontalOffsetProperty); set => SetValue(HorizontalOffsetProperty, value); }
    public Brush WaveformBrush { get => (Brush)GetValue(WaveformBrushProperty); set => SetValue(WaveformBrushProperty, value); }
    public Brush MarkerBrush { get => (Brush)GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public FontFamily? FontFamily { get => (FontFamily?)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public ICommand? SeekCommand { get => (ICommand?)GetValue(SeekCommandProperty); set => SetValue(SeekCommandProperty, value); }
    public IReadOnlyList<int>? Offsets { get => (IReadOnlyList<int>?)GetValue(OffsetsProperty); set => SetValue(OffsetsProperty, value); }
    public IReadOnlyList<int>? Bpms { get => (IReadOnlyList<int>?)GetValue(BpmsProperty); set => SetValue(BpmsProperty, value); }
    public int Subdivision { get => (int)GetValue(SubdivisionProperty); set => SetValue(SubdivisionProperty, value); }

    public WaveformView() => FlowDirection = FlowDirection.LeftToRight;

    protected override Size MeasureOverride(Size availableSize)
    {
        var viewport = IsPositiveFinite(ViewportWidth) ? ViewportWidth : 0;
        var zoom = IsPositiveFinite(ZoomFactor) ? Math.Clamp(ZoomFactor, 1, 16) : 1;
        var height = double.IsFinite(availableSize.Height) ? Math.Max(0, availableSize.Height) : 160;
        return new Size(viewport * zoom, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var data = Data;
        var width = ActualWidth;
        var height = ActualHeight;
        if (width > 0 && height > 0) drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        if (data is null || data.Peaks.Count == 0 || data.Duration <= TimeSpan.Zero || width <= 0 || height <= 0) return;

        var viewport = IsPositiveFinite(ViewportWidth) ? Math.Min(ViewportWidth, width) : width;
        var offset = double.IsFinite(HorizontalOffset) ? Math.Clamp(HorizontalOffset, 0, Math.Max(0, width - viewport)) : 0;
        var waveformBrush = SystemParameters.HighContrast ? SystemColors.HighlightBrush : WaveformBrush;
        var markerBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : MarkerBrush;
        var textBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : TextBrush;
        var baselineY = Math.Max(12, (height - 22) / 2);
        var amplitude = Math.Max(0, baselineY - 8);
        var waveformPen = new Pen(waveformBrush, 1);
        var markerPen = new Pen(markerBrush, 1.5);
        var baselinePen = new Pen(textBrush, 1) { DashStyle = DashStyles.Dot };

        drawingContext.PushClip(new RectangleGeometry(new Rect(offset, 0, viewport, height)));
        BeatGridDrawing.Draw(drawingContext, this, Offsets, Bpms, Subdivision, data.Duration.TotalMilliseconds,
            width, offset, viewport, Math.Max(0, height - 20), true);
        drawingContext.DrawLine(baselinePen, new Point(offset, baselineY), new Point(offset + viewport, baselineY));
        var startPixel = Math.Max(0, (int)Math.Floor(offset));
        var endPixel = Math.Min((int)Math.Ceiling(width), (int)Math.Ceiling(offset + viewport));
        for (var pixel = startPixel; pixel < endPixel; pixel++)
        {
            var peakStart = Math.Clamp((int)(pixel / width * data.Peaks.Count), 0, data.Peaks.Count - 1);
            var peakEnd = Math.Clamp((int)Math.Ceiling((pixel + 1) / width * data.Peaks.Count), peakStart + 1, data.Peaks.Count);
            var peak = 0f;
            for (var index = peakStart; index < peakEnd; index++) peak = Math.Max(peak, data.Peaks[index]);
            var extent = peak * amplitude;
            drawingContext.DrawLine(waveformPen, new Point(pixel + 0.5, baselineY - extent), new Point(pixel + 0.5, baselineY + extent));
        }

        var pixelsPerSecond = width / data.Duration.TotalSeconds;
        var markerX = Math.Clamp(Position.TotalSeconds, 0, data.Duration.TotalSeconds) * pixelsPerSecond;
        markerX = Math.Clamp(markerX, markerPen.Thickness / 2, Math.Max(markerPen.Thickness / 2, width - markerPen.Thickness / 2));
        drawingContext.DrawLine(markerPen, new Point(markerX, 0), new Point(markerX, Math.Max(0, height - 20)));
        DrawRuler(drawingContext, offset, viewport, height, pixelsPerSecond, data.Duration, textBrush);
        drawingContext.Pop();
    }

    private void DrawRuler(DrawingContext drawingContext, double offset, double viewport, double height,
        double pixelsPerSecond, TimeSpan duration, Brush textBrush)
    {
        if (FontFamily is null) return;
        var interval = ChooseTickInterval(pixelsPerSecond);
        var first = Math.Ceiling(offset / pixelsPerSecond / interval) * interval;
        var last = Math.Min(duration.TotalSeconds, (offset + viewport) / pixelsPerSecond);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var pen = new Pen(textBrush, 1);
        for (var seconds = first; seconds <= last; seconds += interval)
        {
            var x = seconds * pixelsPerSecond;
            drawingContext.DrawLine(pen, new Point(x, height - 20), new Point(x, height - 15));
            var text = new FormattedText(FormatTime(seconds), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                10, textBrush, pixelsPerDip);
            drawingContext.DrawText(text, new Point(x + 3, height - 16));
        }
    }

    private static double ChooseTickInterval(double pixelsPerSecond)
    {
        if (!IsPositiveFinite(pixelsPerSecond)) return 1;
        var targetSeconds = 72 / pixelsPerSecond;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(targetSeconds, 0.001))));
        foreach (var multiplier in new[] { 1d, 2d, 5d, 10d })
            if (magnitude * multiplier >= targetSeconds) return magnitude * multiplier;
        return magnitude * 10;
    }

    private static string FormatTime(double seconds) =>
        $"{(int)(seconds / 60):00}:{(int)(seconds % 60):00}";

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;

    public TimeSpan? GetPositionAt(double contentX)
    {
        var data = Data;
        if (data is null || data.Duration <= TimeSpan.Zero || !IsPositiveFinite(ActualWidth) || !double.IsFinite(contentX)) return null;
        return TimeSpan.FromSeconds(Math.Clamp(contentX / ActualWidth, 0, 1) * data.Duration.TotalSeconds);
    }

    public bool SeekAt(double contentX)
    {
        var position = GetPositionAt(contentX);
        var command = SeekCommand;
        if (position is null || command is null || !command.CanExecute(position.Value)) return false;
        command.Execute(position.Value);
        return true;
    }
}
