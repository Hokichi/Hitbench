using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace osuHitsounder.Resources.CustomControls;

public sealed class VolumeWheel : Slider
{
    private static readonly DependencyPropertyKey FilledGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(FilledGeometry), typeof(Geometry), typeof(VolumeWheel), new PropertyMetadata(Geometry.Empty));

    public static readonly DependencyProperty FilledGeometryProperty = FilledGeometryPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(VolumeWheel), new FrameworkPropertyMetadata(false, OnVisualStateChanged));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(VolumeWheel), new FrameworkPropertyMetadata(string.Empty, OnLabelChanged));
    public static readonly DependencyProperty PieOpacityProperty = DependencyProperty.Register(
        nameof(PieOpacity), typeof(double), typeof(VolumeWheel), new PropertyMetadata(0.75));

    static VolumeWheel()
    {
        MinimumProperty.OverrideMetadata(typeof(VolumeWheel), new FrameworkPropertyMetadata(0d, OnValueRangeChanged));
        MaximumProperty.OverrideMetadata(typeof(VolumeWheel), new FrameworkPropertyMetadata(100d, OnValueRangeChanged));
        ValueProperty.OverrideMetadata(typeof(VolumeWheel), new FrameworkPropertyMetadata(100d, OnValueRangeChanged));
        SmallChangeProperty.OverrideMetadata(typeof(VolumeWheel), new FrameworkPropertyMetadata(5d));
        LargeChangeProperty.OverrideMetadata(typeof(VolumeWheel), new FrameworkPropertyMetadata(5d));
    }

    public Geometry FilledGeometry => (Geometry)GetValue(FilledGeometryProperty);
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double PieOpacity { get => (double)GetValue(PieOpacityProperty); private set => SetValue(PieOpacityProperty, value); }

    public VolumeWheel() => UpdateGeometry();

    protected override AutomationPeer OnCreateAutomationPeer() => new VolumeWheelAutomationPeer(this);

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (!IsActive && e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right or
            System.Windows.Input.Key.Up or System.Windows.Input.Key.Down or System.Windows.Input.Key.Home or
            System.Windows.Input.Key.End or System.Windows.Input.Key.PageUp or System.Windows.Input.Key.PageDown)
            e.Handled = true;
        base.OnPreviewKeyDown(e);
    }

    private static void OnVisualStateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((VolumeWheel)sender).PieOpacity = (bool)e.NewValue ? 1 : 0.75;

    private static void OnLabelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        AutomationProperties.SetName((VolumeWheel)sender, $"{e.NewValue} volume");

    private static void OnValueRangeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((VolumeWheel)sender).UpdateGeometry();

    private void UpdateGeometry()
    {
        var range = Maximum - Minimum;
        var fraction = range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0, 1);
        if (fraction <= 0)
        {
            SetValue(FilledGeometryPropertyKey, Geometry.Empty);
            return;
        }
        if (fraction >= 1)
        {
            SetValue(FilledGeometryPropertyKey, new EllipseGeometry(new Point(36, 36), 32, 32));
            return;
        }

        var angle = fraction * Math.PI * 2 - Math.PI / 2;
        var end = new Point(36 + Math.Cos(angle) * 32, 36 + Math.Sin(angle) * 32);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(36, 36), true, true);
            context.LineTo(new Point(36, 4), true, false);
            context.ArcTo(end, new Size(32, 32), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        SetValue(FilledGeometryPropertyKey, geometry);
    }
}
