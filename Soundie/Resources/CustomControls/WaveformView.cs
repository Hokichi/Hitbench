using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Soundie.Core.Entities;
using Soundie.Services;

namespace Soundie.Resources.CustomControls;

public sealed class WaveformView : FrameworkElement
{
    public static readonly DependencyProperty ShowPlaybackMarkerProperty = DependencyProperty.Register(nameof(ShowPlaybackMarker), typeof(bool), typeof(WaveformView), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool ShowPlaybackMarker { get => (bool)GetValue(ShowPlaybackMarkerProperty); set => SetValue(ShowPlaybackMarkerProperty,value); }
    public static readonly DependencyProperty CenterWaveformProperty = DependencyProperty.Register(nameof(CenterWaveform), typeof(bool), typeof(WaveformView), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool CenterWaveform { get => (bool)GetValue(CenterWaveformProperty); set => SetValue(CenterWaveformProperty,value); }
    public static readonly DependencyProperty BarCountProperty = DependencyProperty.Register(nameof(BarCount), typeof(int), typeof(WaveformView), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public int BarCount { get => (int)GetValue(BarCountProperty); set => SetValue(BarCountProperty, value); }
    public static readonly DependencyProperty BarThicknessProperty = DependencyProperty.Register(nameof(BarThickness), typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double BarThickness { get => (double)GetValue(BarThicknessProperty); set => SetValue(BarThicknessProperty, value); }
    public static readonly DependencyProperty MarkerThicknessProperty = DependencyProperty.Register(nameof(MarkerThickness), typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(1.5d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double MarkerThickness { get => (double)GetValue(MarkerThicknessProperty); set => SetValue(MarkerThicknessProperty, value); }
    public static readonly DependencyProperty ShowBeatRulerProperty=DependencyProperty.Register(nameof(ShowBeatRuler),typeof(bool),typeof(WaveformView),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender));
    public bool ShowBeatRuler {get=>(bool)GetValue(ShowBeatRulerProperty);set=>SetValue(ShowBeatRulerProperty,value);}
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
    public static readonly DependencyProperty PlayedWaveformBrushProperty = DependencyProperty.Register(
        nameof(PlayedWaveformBrush), typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(SystemColors.HighlightBrush, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush PlayedWaveformBrush { get => (Brush)GetValue(PlayedWaveformBrushProperty); set => SetValue(PlayedWaveformBrushProperty, value); }
    private readonly RectangleGeometry playedClip = new();

    public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(
        nameof(MarkerBrush), typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(SystemColors.HighlightTextBrush, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ControlPointTimestampsProperty = DependencyProperty.Register(
        nameof(ControlPointTimestamps), typeof(IReadOnlyList<int>), typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ControlPointBrushProperty = DependencyProperty.Register(
        nameof(ControlPointBrush), typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(SystemColors.WindowTextBrush, FrameworkPropertyMetadataOptions.AffectsRender));
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
    public IReadOnlyList<int>? ControlPointTimestamps { get => (IReadOnlyList<int>?)GetValue(ControlPointTimestampsProperty); set => SetValue(ControlPointTimestampsProperty, value); }
    public Brush ControlPointBrush { get => (Brush)GetValue(ControlPointBrushProperty); set => SetValue(ControlPointBrushProperty, value); }
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
        if (!ShowPlaybackMarker || Data is null || Data.Duration <= TimeSpan.Zero || Data.Peaks.Count == 0) return;
        var end = new Point(0, ActualHeight);
        if (!SystemParameters.HighContrast && BarCount == 0) context.DrawLine(new Pen(WaveformBrush, 3.5), new Point(), end);
        context.DrawLine(new Pen(SystemParameters.HighContrast ? SystemColors.WindowTextBrush : MarkerBrush, MarkerThickness), new Point(), end);
    }

    private void UpdateMarker()
    {
        if (Data is null || Data.Duration <= TimeSpan.Zero)
        {
            playedClip.Rect = Rect.Empty;
            return;
        }
        var playedWidth = Math.Clamp(Position.TotalSeconds / Data.Duration.TotalSeconds * ActualWidth, 0, Math.Max(0, ActualWidth));
        playedClip.Rect = new Rect(0, 0, playedWidth, Math.Max(0, ActualHeight));
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

        if (BarCount > 0)
        {
            var pen = new Pen(SystemParameters.HighContrast ? SystemColors.HighlightBrush : WaveformBrush, BarThickness);
            var count = Math.Min(BarCount, 4096);
            for (var bar = 0; bar < count; bar++)
            {
                var first = bar * data.Peaks.Count / count;
                var last = Math.Max(first + 1, (bar + 1) * data.Peaks.Count / count);
                var peak = 0f;
                for (var index = first; index < Math.Min(last, data.Peaks.Count); index++) peak = Math.Max(peak, data.Peaks[index]);
                var px = (bar + 0.5) * width / count;
                var extent = Math.Max(1, peak * (height / 2 - 4));
                drawingContext.DrawLine(pen, new Point(px, height / 2 - extent), new Point(px, height / 2 + extent));
            }
            return;
        }

        var viewport = IsPositiveFinite(ViewportWidth) ? Math.Min(ViewportWidth, width) : width;
        var offset = double.IsFinite(HorizontalOffset) ? Math.Clamp(HorizontalOffset, 0, Math.Max(0, width - viewport)) : 0;
        var waveformBrush = SystemParameters.HighContrast ? SystemColors.HighlightBrush : WaveformBrush;
        var textBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : TextBrush;
        var rulerHeight=ShowBeatRuler?24:0;
        var baselineY = CenterWaveform ? rulerHeight + (height-rulerHeight)/2 : rulerHeight+Math.Max(12, (height-rulerHeight - 22) / 2);
        var amplitude = Math.Max(0, baselineY-rulerHeight - 8);
        var waveformPen = new Pen(waveformBrush, 1);
        var baselinePen = new Pen(textBrush, 1) { DashStyle = DashStyles.Dot };

        drawingContext.PushClip(new RectangleGeometry(new Rect(offset, 0, viewport, height)));
        BeatGridDrawing.Draw(drawingContext, this, Offsets, Bpms, Subdivision, data.Duration.TotalMilliseconds,
            width, offset, viewport, height, true);
        drawingContext.DrawLine(baselinePen, new Point(offset, baselineY), new Point(offset + viewport, baselineY));
        var startPixel = Math.Max(0, (int)Math.Floor(offset));
        var endPixel = Math.Min((int)Math.Ceiling(width), (int)Math.Ceiling(offset + viewport));
        var peaks = new StreamGeometry();
        using (var geometry = peaks.Open())
        for (var pixel = startPixel; pixel < endPixel; pixel++)
        {
            var peakStart = Math.Clamp((int)(pixel / width * data.Peaks.Count), 0, data.Peaks.Count - 1);
            var peakEnd = Math.Clamp((int)Math.Ceiling((pixel + 1) / width * data.Peaks.Count), peakStart + 1, data.Peaks.Count);
            var peak = 0f;
            for (var index = peakStart; index < peakEnd; index++) peak = Math.Max(peak, data.Peaks[index]);
            var extent = peak * amplitude;
            geometry.BeginFigure(new Point(pixel + 0.5, baselineY - extent), false, false);
            geometry.LineTo(new Point(pixel + 0.5, baselineY + extent), true, false);
        }

        peaks.Freeze();
        drawingContext.DrawGeometry(null, waveformPen, peaks);
        var played = new DrawingGroup { ClipGeometry = playedClip };
        played.Children.Add(new GeometryDrawing(null,
            new Pen(SystemParameters.HighContrast ? SystemColors.HighlightBrush : PlayedWaveformBrush, 1), peaks));
        drawingContext.DrawDrawing(played);

        if(ShowBeatRuler && Offsets is not null && Bpms is not null)
        {
            var lastLabelRight=double.NegativeInfinity;
            foreach(var beat in BeatGridCalculator.GetLines(Offsets,Bpms,1,offset/width*data.Duration.TotalMilliseconds,(offset+viewport)/width*data.Duration.TotalMilliseconds))
            {
                var labelX=beat.Milliseconds/data.Duration.TotalMilliseconds*width+4;
                if(labelX<lastLabelRight+8)continue;
                var label=new FormattedText(string.Format(System.Globalization.CultureInfo.CurrentCulture,"{0:0.###} s",beat.Milliseconds/1000),
                    System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,
                    new Typeface(FontFamily??TryFindResource("Regular") as FontFamily??SystemFonts.MessageFontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),
                    11,textBrush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                drawingContext.DrawText(label,new Point(labelX,4));
                lastLabelRight=labelX+label.Width;
            }
        }
        if (ControlPointTimestamps is { } timestamps)
        {
            var pointPen = new Pen(SystemParameters.HighContrast ? SystemColors.WindowTextBrush : ControlPointBrush, 1);
            foreach (var timestamp in timestamps.Distinct())
            {
                if (timestamp < 0 || timestamp > data.Duration.TotalMilliseconds) continue;
                var x = timestamp / data.Duration.TotalMilliseconds * width;
                if (x < offset || x > offset + viewport) continue;
                drawingContext.DrawLine(pointPen, new Point(x, Math.Min(rulerHeight, height)), new Point(x, height));
            }
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
