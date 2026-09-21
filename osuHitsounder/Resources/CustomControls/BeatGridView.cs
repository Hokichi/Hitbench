using System.Windows;
using System.Windows.Media;

namespace osuHitsounder.Resources.CustomControls;

public sealed class BeatGridView : FrameworkElement
{
    public static readonly DependencyProperty OffsetsProperty = DependencyProperty.Register(nameof(Offsets), typeof(IReadOnlyList<int>), typeof(BeatGridView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BpmsProperty = DependencyProperty.Register(nameof(Bpms), typeof(IReadOnlyList<int>), typeof(BeatGridView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SubdivisionProperty = DependencyProperty.Register(nameof(Subdivision), typeof(int), typeof(BeatGridView), new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(nameof(Duration), typeof(TimeSpan), typeof(BeatGridView), new FrameworkPropertyMetadata(TimeSpan.Zero, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ContentWidthProperty = DependencyProperty.Register(nameof(ContentWidth), typeof(double), typeof(BeatGridView), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(BeatGridView), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(nameof(ViewportWidth), typeof(double), typeof(BeatGridView), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<int>? Offsets { get => (IReadOnlyList<int>?)GetValue(OffsetsProperty); set => SetValue(OffsetsProperty, value); }
    public IReadOnlyList<int>? Bpms { get => (IReadOnlyList<int>?)GetValue(BpmsProperty); set => SetValue(BpmsProperty, value); }
    public int Subdivision { get => (int)GetValue(SubdivisionProperty); set => SetValue(SubdivisionProperty, value); }
    public TimeSpan Duration { get => (TimeSpan)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public double ContentWidth { get => (double)GetValue(ContentWidthProperty); set => SetValue(ContentWidthProperty, value); }
    public double HorizontalOffset { get => (double)GetValue(HorizontalOffsetProperty); set => SetValue(HorizontalOffsetProperty, value); }
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }

    public BeatGridView() => FlowDirection = FlowDirection.LeftToRight;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : 0,
            double.IsFinite(availableSize.Height) ? Math.Max(0, availableSize.Height) : 72);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        BeatGridDrawing.Draw(drawingContext, this, Offsets, Bpms, Subdivision, Duration.TotalMilliseconds,
            ContentWidth, HorizontalOffset, ViewportWidth, ActualHeight, false);
    }
}
