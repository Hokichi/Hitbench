using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace osuHitsounder.Resources.CustomControls;

/// <summary>
///     Interaction logic for BalloonBorder.xaml
/// </summary>
public partial class BalloonBorder : UserControl
{
    // --- CurveHeight DP ---
    public static readonly DependencyProperty CurveHeightProperty =
        DependencyProperty.Register(
            nameof(CurveHeight), typeof(double), typeof(BalloonBorder),
            new FrameworkPropertyMetadata(13.0, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryInvalidated));

    // --- CornerRadius DP ---
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius), typeof(double), typeof(BalloonBorder),
            new FrameworkPropertyMetadata(15.0, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryInvalidated));

    // --- BalloonFill DP (forwards to Path) ---
    public static readonly DependencyProperty BalloonFillProperty =
        DependencyProperty.Register(
            nameof(BalloonFill), typeof(Brush), typeof(BalloonBorder),
            new PropertyMetadata(Brushes.CornflowerBlue, (d, _) => ((BalloonBorder)d).ApplyFill()));

    // -------------------------------------------------------

    public BalloonBorder()
    {
        InitializeComponent();
        SizeChanged += (_, _) => RebuildGeometry();
        Loaded += (_, _) => RebuildGeometry();
    }

    public double CurveHeight
    {
        get => (double)GetValue(CurveHeightProperty);
        set => SetValue(CurveHeightProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Brush BalloonFill
    {
        get => (Brush)GetValue(BalloonFillProperty);
        set => SetValue(BalloonFillProperty, value);
    }

    private static void OnGeometryInvalidated(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BalloonBorder)d).RebuildGeometry();
    }

    private void ApplyFill()
    {
        PART_Shape.Fill = BalloonFill;
    }

    private void RebuildGeometry()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var c = CurveHeight; // how far the sides puff out
        var r = CornerRadius;

        if (w <= 0 || h <= 0) return;

        // Keep content inset so it clears the bulging edges
        var inset = c + r * 0.5;
        PART_Content.Margin = new Thickness(inset);
        PART_Shape.Fill = BalloonFill;

        // Build the figure clockwise starting at top-left corner end
        var figure = new PathFigure { StartPoint = new Point(r, 0), IsClosed = true };

        // Top side — bulges upward (control points above y=0)
        figure.Segments.Add(new BezierSegment(new Point(w * 0.25, -c), new Point(w * 0.75, -c), new Point(w - r, 0),
            true));
        // Top-right corner
        figure.Segments.Add(new ArcSegment(new Point(w, r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        // Right side — bulges rightward (control points beyond x=w)
        figure.Segments.Add(new BezierSegment(new Point(w + c, h * 0.25), new Point(w + c, h * 0.75),
            new Point(w, h - r), true));
        // Bottom-right corner
        figure.Segments.Add(new ArcSegment(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise,
            true));
        // Bottom side — bulges downward (control points below y=h)
        figure.Segments.Add(new BezierSegment(new Point(w * 0.75, h + c), new Point(w * 0.25, h + c), new Point(r, h),
            true));
        // Bottom-left corner
        figure.Segments.Add(new ArcSegment(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise,
            true));
        // Left side — bulges leftward (control points before x=0)
        figure.Segments.Add(new BezierSegment(new Point(-c, h * 0.75), new Point(-c, h * 0.25), new Point(0, r), true));
        // Top-left corner (back to start)
        figure.Segments.Add(new ArcSegment(new Point(r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise, true));

        PART_Shape.Data = new PathGeometry(new[] { figure });
    }
}

