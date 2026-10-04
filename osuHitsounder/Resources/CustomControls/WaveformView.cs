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
        nameof(Position), typeof(TimeSpan), typeof(WaveformView),
        new FrameworkPropertyMetadata(TimeSpan.Zero, (sender, _) => ((WaveformView)sender).UpdateMarker()));
    public static readonly DependencyProperty ZoomFactorProperty = DependencyProperty.Register(
        nameof(ZoomFactor), typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
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

    public WaveformView() { FlowDirection = FlowDirection.LeftToRight; InitializeDrawing(); }


    private readonly DrawingVisual contentVisual = new();
    private readonly DrawingVisual markerVisual = new();
    private readonly TranslateTransform markerTransform = new();
    private readonly RectangleGeometry markerClip = new();
    private bool contentDirty = true;
    internal DrawingGroup? ContentDrawing { get; private set; }
    protected override int VisualChildrenCount => 2;
    protected override Visual GetVisualChild(int index) => index switch
    { 0 => contentVisual, 1 => markerVisual, _ => throw new ArgumentOutOfRangeException(nameof(index)) };

    private void InitializeDrawing()
    {
        AddVisualChild(contentVisual);
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
        if (e.Property != PositionProperty && e.Property.GetMetadata(GetType()) is FrameworkPropertyMetadata metadata &&
            (metadata.AffectsRender || metadata.AffectsMeasure)) InvalidateContent();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    { base.OnRenderSizeChanged(info); InvalidateContent(); }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    { base.OnDpiChanged(oldDpi, newDpi); InvalidateContent(); }
    private void InvalidateContent() { contentDirty = true; InvalidateVisual(); }

    internal void PrepareDrawing()
    {
        if (!contentDirty) return;
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) DrawContent(context);
        ContentDrawing = drawing;
        using (var context = contentVisual.RenderOpen()) context.DrawDrawing(drawing);
        using (var context = markerVisual.RenderOpen()) DrawMarker(context);
        contentDirty = false;
        UpdateMarker();
    }

    protected override void OnRender(DrawingContext context) { base.OnRender(context); PrepareDrawing(); }

    private void DrawMarker(DrawingContext context)
    {
        if (Data is null || Data.Duration <= TimeSpan.Zero || Data.Peaks.Count == 0) return;
        var end = new Point(0, Math.Max(0, ActualHeight - 20));
        if (!SystemParameters.HighContrast) context.DrawLine(new Pen(WaveformBrush, 3.5), new Point(), end);
        context.DrawLine(new Pen(SystemParameters.HighContrast ? SystemColors.WindowTextBrush : MarkerBrush, 1.5), new Point(), end);
    }

    private void UpdateMarker()
    {
        if (Data is null || Data.Duration <= TimeSpan.Zero) return;
        var x = Math.Clamp(Position.TotalSeconds / Data.Duration.TotalSeconds * ActualWidth, .75, Math.Max(.75, ActualWidth - .75));
        var viewport = IsPositiveFinite(ViewportWidth) ? Math.Min(ViewportWidth, ActualWidth) : ActualWidth;
        var offset = double.IsFinite(HorizontalOffset) ? Math.Clamp(HorizontalOffset, 0, Math.Max(0, ActualWidth - viewport)) : 0;
        markerTransform.X = x;
        markerClip.Rect = new Rect(offset - x, 0, viewport, ActualHeight);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var viewport = IsPositiveFinite(ViewportWidth) ? ViewportWidth : 0;
        var zoom = IsPositiveFinite(ZoomFactor) ? Math.Clamp(ZoomFactor, 8, 128) : 8;
        var height = double.IsFinite(availableSize.Height) ? Math.Max(0, availableSize.Height) : 160;
        return new Size(viewport * zoom, height);
    }

    private void DrawContent(DrawingContext drawingContext)
    {
        var data = Data;
        var width = ActualWidth;
        var height = ActualHeight;
        if (width > 0 && height > 0) drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        if (data is null || data.Peaks.Count == 0 || data.Duration <= TimeSpan.Zero || width <= 0 || height <= 0) return;

        var viewport = IsPositiveFinite(ViewportWidth) ? Math.Min(ViewportWidth, width) : width;
        var offset = double.IsFinite(HorizontalOffset) ? Math.Clamp(HorizontalOffset, 0, Math.Max(0, width - viewport)) : 0;
        var waveformBrush = SystemParameters.HighContrast ? SystemColors.HighlightBrush : WaveformBrush;
        var textBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : TextBrush;
        var baselineY = Math.Max(12, (height - 22) / 2);
        var amplitude = Math.Max(0, baselineY - 8);
        var waveformPen = new Pen(waveformBrush, 1);
        var baselinePen = new Pen(textBrush, 1) { DashStyle = DashStyles.Dot };

        drawingContext.PushClip(new RectangleGeometry(new Rect(offset, 0, viewport, height)));
        BeatGridDrawing.Draw(drawingContext, this, Offsets, Bpms, Subdivision, data.Duration.TotalMilliseconds,
            width, offset, viewport, height, true);
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

        drawingContext.Pop();
    }

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
