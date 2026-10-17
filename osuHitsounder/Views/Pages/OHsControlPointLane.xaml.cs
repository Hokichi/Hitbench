using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Views.Pages;

public partial class OHsControlPointLane : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(ControlPointsSnapshot), typeof(OHsControlPointLane), new PropertyMetadata(null, OnStateChanged));
    public static readonly DependencyProperty DurationMillisecondsProperty = RegisterCoordinate(nameof(DurationMilliseconds));
    public static readonly DependencyProperty ContentWidthProperty = RegisterCoordinate(nameof(ContentWidth));
    public static readonly DependencyProperty ViewportWidthProperty = RegisterCoordinate(nameof(ViewportWidth));
    public static readonly DependencyProperty HorizontalOffsetProperty = RegisterCoordinate(nameof(HorizontalOffset));
    private static readonly DependencyPropertyKey IsMenuOwnerActivePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsMenuOwnerActive), typeof(bool), typeof(OHsControlPointLane), new PropertyMetadata(false));
    public static readonly DependencyProperty IsMenuOwnerActiveProperty = IsMenuOwnerActivePropertyKey.DependencyProperty;
    public ControlPointsSnapshot? State { get => (ControlPointsSnapshot?)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public double DurationMilliseconds { get => (double)GetValue(DurationMillisecondsProperty); set => SetValue(DurationMillisecondsProperty, value); }
    public double ContentWidth { get => (double)GetValue(ContentWidthProperty); set => SetValue(ContentWidthProperty, value); }
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double HorizontalOffset { get => (double)GetValue(HorizontalOffsetProperty); set => SetValue(HorizontalOffsetProperty, value); }
    public bool IsMenuOwnerActive => (bool)GetValue(IsMenuOwnerActiveProperty);
    private OHsControlPointLanePanel? panel;
    private OHsControlPointTag? measurementTag;
    private string? measuredCulture;
    public event Action<double>? HorizontalOffsetRequested;
    internal int RealizedPointCount => EnsurePanel()?.RealizedPointCount ?? 0;
    internal int RecycledPointCount => EnsurePanel()?.RecycledPointCount ?? 0;

    public OHsControlPointLane() => InitializeComponent();
    private static DependencyProperty RegisterCoordinate(string name) => DependencyProperty.Register(name, typeof(double), typeof(OHsControlPointLane),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, e) =>
        {
            var lane = (OHsControlPointLane)d;
            lane.SyncPanel();
            if (e.Property == ViewportWidthProperty) lane.panel?.InvalidateGeometry();
        }));

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var lane = (OHsControlPointLane)d;
        lane.SetValue(IsMenuOwnerActivePropertyKey, lane.State?.CanEdit == true && lane.IsEnabled);
        var points = lane.State?.Points ?? [];
        if (lane.PointItems.ItemsSource is not IReadOnlyList<ControlPointSnapshot> previous || !previous.SequenceEqual(points))
            lane.PointItems.ItemsSource = points;
        lane.panel?.InvalidateMeasure();
    }

    private OHsControlPointLanePanel? EnsurePanel()
    {
        PointItems.ApplyTemplate();
        panel ??= FindChild<OHsControlPointLanePanel>(PointItems);
        if (panel is not null) { panel.MeasurePoint = MeasurePoint; SyncPanel(); }
        return panel;
    }

    private Size MeasurePoint(ControlPointSnapshot point)
    {
        measurementTag ??= new() { Style = (Style)FindResource("OHsControlPointTagStyle") };
        measurementTag.Point = point; measurementTag.RefreshText(); measurementTag.FlowDirection = FlowDirection;
        measurementTag.MaxWidth = Math.Max(0, ViewportWidth);
        measurementTag.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        return measurementTag.DesiredSize;
    }

    private void SyncPanel()
    {
        if (panel is null) return;
        panel.DurationMilliseconds = DurationMilliseconds; panel.ContentWidth = ContentWidth;
        panel.ViewportWidth = ViewportWidth; panel.HorizontalOffset = HorizontalOffset;
        if (measuredCulture != CultureInfo.CurrentCulture.Name) { measuredCulture = CultureInfo.CurrentCulture.Name; panel.InvalidateGeometry(); }
        panel.InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        EnsurePanel();
        var result = base.MeasureOverride(constraint);
        if (EnsurePanel() is { } current && current.RealizedPointCount == 0 && State?.Points.Count > 0)
        { current.InvalidateMeasure(); result = base.MeasureOverride(constraint); }
        return result;
    }

    internal IReadOnlyList<OHsControlPointExtent> GetExtents() => EnsurePanel()?.GetExtents(true) ?? [];

    public OHsControlPointTag? BringPointIntoView(Guid id)
    {
        if (EnsurePanel() is not { } current || GetExtents().FirstOrDefault(p => p.Id == id) is not { } extent) return null;
        current.PinnedPointId = id;
        var offset = extent.Left < HorizontalOffset ? extent.Left : extent.Left + extent.Width > HorizontalOffset + ViewportWidth
            ? extent.Left + extent.Width - ViewportWidth : HorizontalOffset;
        HorizontalOffsetRequested?.Invoke(Math.Clamp(offset, 0, Math.Max(0, ContentWidth - ViewportWidth)));
        current.InvalidateMeasure(); PointItems.UpdateLayout();
        var index = State!.Points.ToList().FindIndex(p => p.Id == id);
        return PointItems.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container ? FindChild<OHsControlPointTag>(container) : null;
    }

    internal OHsControlPointTag? MovePointFocus(Guid currentId, bool forward)
    {
        if (State is null) return null;
        var index = State.Points.ToList().FindIndex(p => p.Id == currentId);
        var next = index + (forward ? 1 : -1);
        return index >= 0 && next >= 0 && next < State.Points.Count ? BringPointIntoView(State.Points[next].Id) : null;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Tab && Keyboard.FocusedElement is OHsControlPointTag { Point: { } point } &&
            MovePointFocus(point.Id, (Keyboard.Modifiers & ModifierKeys.Shift) == 0) is { } next)
        { next.Focus(); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }

    internal static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T result) return result;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindChild<T>(VisualTreeHelper.GetChild(root, index)) is { } child) return child;
        return null;
    }
}
