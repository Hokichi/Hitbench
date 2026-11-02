using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Hitbench.Resources.DataModels;

namespace Hitbench.Views.Pages;

/// <summary>A temporal zone retaining native button invocation and view-only volume preview.</summary>
public sealed class OHsControlPointZone : Button
{
    public static readonly DependencyProperty CurveProperty = DependencyProperty.Register(nameof(Curve), typeof(OHsControlPointCurveProjection), typeof(OHsControlPointZone),
        new PropertyMetadata(null, (d, _) => ((OHsControlPointZone)d).RefreshText()));
    public OHsControlPointCurveProjection? Curve { get => (OHsControlPointCurveProjection?)GetValue(CurveProperty); set { if (!Equals(Curve, value)) SetValue(CurveProperty, value); } }
    private static readonly DependencyPropertyKey FillGeometryPropertyKey = DependencyProperty.RegisterReadOnly(nameof(FillGeometry), typeof(System.Windows.Media.Geometry), typeof(OHsControlPointZone), new PropertyMetadata(null));
    public static readonly DependencyProperty FillGeometryProperty = FillGeometryPropertyKey.DependencyProperty;
    public System.Windows.Media.Geometry? FillGeometry => (System.Windows.Media.Geometry?)GetValue(FillGeometryProperty);
    private static readonly DependencyPropertyKey OutlineGeometryPropertyKey = DependencyProperty.RegisterReadOnly(nameof(OutlineGeometry), typeof(System.Windows.Media.Geometry), typeof(OHsControlPointZone), new PropertyMetadata(null));
    public static readonly DependencyProperty OutlineGeometryProperty = OutlineGeometryPropertyKey.DependencyProperty;
    public System.Windows.Media.Geometry? OutlineGeometry => (System.Windows.Media.Geometry?)GetValue(OutlineGeometryProperty);
    private static readonly DependencyPropertyKey EndLineOffsetPropertyKey = DependencyProperty.RegisterReadOnly(nameof(EndLineOffset), typeof(double), typeof(OHsControlPointZone), new PropertyMetadata(0d));
    public static readonly DependencyProperty EndLineOffsetProperty = EndLineOffsetPropertyKey.DependencyProperty;
    public double EndLineOffset => (double)GetValue(EndLineOffsetProperty);
    private static readonly DependencyPropertyKey ConnectorStartOffsetPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ConnectorStartOffset), typeof(double), typeof(OHsControlPointZone), new PropertyMetadata(0d));
    public static readonly DependencyProperty ConnectorStartOffsetProperty = ConnectorStartOffsetPropertyKey.DependencyProperty;
    public double ConnectorStartOffset => (double)GetValue(ConnectorStartOffsetProperty);
    public static readonly DependencyProperty PointProperty = DependencyProperty.Register(nameof(Point), typeof(ControlPointSnapshot), typeof(OHsControlPointZone),
        new PropertyMetadata(null, (d, _) => { var zone = (OHsControlPointZone)d; zone.Curve = null; zone.PreviewVolume = null; zone.IsVolumeDragging = false; zone.RefreshText(); }));
    public static readonly DependencyProperty PreviewVolumeProperty = DependencyProperty.Register(nameof(PreviewVolume), typeof(int?), typeof(OHsControlPointZone),
        new PropertyMetadata(null, (d, _) => ((OHsControlPointZone)d).RefreshText()));
    public static readonly DependencyProperty IsVolumeDraggingProperty = DependencyProperty.Register(nameof(IsVolumeDragging), typeof(bool), typeof(OHsControlPointZone));
    public static readonly DependencyProperty IsInvalidProperty = DependencyProperty.Register(nameof(IsInvalid), typeof(bool), typeof(OHsControlPointZone));
    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(nameof(IsEditing), typeof(bool), typeof(OHsControlPointZone));
    public static readonly DependencyProperty IsMenuOwnerActiveProperty = DependencyProperty.Register(nameof(IsMenuOwnerActive), typeof(bool), typeof(OHsControlPointZone));
    private static readonly DependencyPropertyKey LabelPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Label), typeof(string), typeof(OHsControlPointZone), new PropertyMetadata(""));
    public static readonly DependencyProperty LabelProperty = LabelPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey FillHeightPropertyKey = DependencyProperty.RegisterReadOnly(nameof(FillHeight), typeof(double), typeof(OHsControlPointZone), new PropertyMetadata(0d));
    public static readonly DependencyProperty FillHeightProperty = FillHeightPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey VolumeLineOffsetPropertyKey = DependencyProperty.RegisterReadOnly(nameof(VolumeLineOffset), typeof(double), typeof(OHsControlPointZone), new PropertyMetadata(0d));
    public static readonly DependencyProperty VolumeLineOffsetProperty = VolumeLineOffsetPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey VisibleLabelInsetPropertyKey = DependencyProperty.RegisterReadOnly(nameof(VisibleLabelInset), typeof(double), typeof(OHsControlPointZone), new PropertyMetadata(0d));
    public static readonly DependencyProperty VisibleLabelInsetProperty = VisibleLabelInsetPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey VisibleLabelWidthPropertyKey = DependencyProperty.RegisterReadOnly(nameof(VisibleLabelWidth), typeof(double), typeof(OHsControlPointZone), new PropertyMetadata(0d));
    public static readonly DependencyProperty VisibleLabelWidthProperty = VisibleLabelWidthPropertyKey.DependencyProperty;
    public ControlPointSnapshot? Point { get => (ControlPointSnapshot?)GetValue(PointProperty); set => SetValue(PointProperty, value); }
    public int? PreviewVolume { get => (int?)GetValue(PreviewVolumeProperty); set => SetValue(PreviewVolumeProperty, value); }
    public bool IsVolumeDragging { get => (bool)GetValue(IsVolumeDraggingProperty); set => SetValue(IsVolumeDraggingProperty, value); }
    public bool IsInvalid { get => (bool)GetValue(IsInvalidProperty); set => SetValue(IsInvalidProperty, value); }
    public bool IsEditing { get => (bool)GetValue(IsEditingProperty); set => SetValue(IsEditingProperty, value); }
    public bool IsMenuOwnerActive { get => (bool)GetValue(IsMenuOwnerActiveProperty); set => SetValue(IsMenuOwnerActiveProperty, value); }
    public string Label => (string)GetValue(LabelProperty);
    public double FillHeight => (double)GetValue(FillHeightProperty);
    public double VolumeLineOffset => (double)GetValue(VolumeLineOffsetProperty);
    public double VisibleLabelInset => (double)GetValue(VisibleLabelInsetProperty);
    public double VisibleLabelWidth => (double)GetValue(VisibleLabelWidthProperty);

    internal void SetVisibleLabelRange(double inset, double width)
    {
        SetValue(VisibleLabelInsetPropertyKey, Math.Max(0, inset));
        SetValue(VisibleLabelWidthPropertyKey, Math.Max(0, width - 16));
    }

    internal void RefreshText()
    {
        var volume = PreviewVolume ?? Point?.Volume ?? 0;
        var text = Point is { } point ? string.Format(CultureInfo.CurrentCulture, "{0} · {1}%", point.Sampleset, volume) : "";
        SetValue(LabelPropertyKey, text);
        var height = Math.Max(0, ActualHeight);
        double Y(double value) => height * (1 - Math.Clamp(value, 0, 100) / 100);
        var startY = Y(Curve?.StartVolume ?? volume);
        var endY = Y(Curve?.EndVolume ?? volume);
        SetValue(EndLineOffsetPropertyKey, endY);
        SetValue(ConnectorStartOffsetPropertyKey, Y(Curve?.ConnectorFromVolume ?? Curve?.StartVolume ?? volume));
        var shape = new System.Windows.Media.StreamGeometry();
        using (var drawing = shape.Open())
        {
            drawing.BeginFigure(new Point(0, startY), true, true);
            drawing.LineTo(new Point(ActualWidth, endY), true, false);
            drawing.LineTo(new Point(ActualWidth, height), true, false);
            drawing.LineTo(new Point(0, height), true, false);
        }
        shape.Freeze(); SetValue(FillGeometryPropertyKey, shape);
        var outline = new System.Windows.Media.StreamGeometry();
        using (var drawing = outline.Open())
        {
            drawing.BeginFigure(new Point(0, Y(Curve?.ConnectorFromVolume ?? Curve?.StartVolume ?? volume)), false, false);
            drawing.LineTo(new Point(0, startY), true, true);
            drawing.LineTo(new Point(ActualWidth, endY), true, true);
        }
        outline.Freeze();
        SetValue(OutlineGeometryPropertyKey, outline);
        var fill = height - startY;
        SetValue(FillHeightPropertyKey, fill);
        SetValue(VolumeLineOffsetPropertyKey, startY);
        AutomationProperties.SetName(this, Point is { } current ? string.Format(CultureInfo.CurrentCulture,
            "{0}, control point at {1} milliseconds, {2}", text, current.Timestamp, (current.IsDisabled ? "disabled" : "enabled") + (current.IsRamp ? ", ramp" : ", fixed")) : "Control point");
        AutomationProperties.SetHelpText(this, Point?.IsOffsetAnchor == true
            ? "Click to edit. Drag vertically to change volume. Up/Down adjusts volume."
            : "Click to edit. Drag vertically to change volume; drag the left or right boundary horizontally to move timing. Up/Down adjusts volume; Alt+Left/Right adjusts timing.");
        ToolTip = text;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RefreshText();
    }
}
