using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Helpers;

namespace Hitbench.Views.Pages;

public partial class OHsControlPointLane : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(ControlPointsSnapshot), typeof(OHsControlPointLane), new PropertyMetadata(null, OnStateChanged));
    public static readonly DependencyProperty DurationMillisecondsProperty = RegisterCoordinate(nameof(DurationMilliseconds));
    public static readonly DependencyProperty ContentWidthProperty = RegisterCoordinate(nameof(ContentWidth));
    public static readonly DependencyProperty ViewportWidthProperty = RegisterCoordinate(nameof(ViewportWidth));
    public static readonly DependencyProperty HorizontalOffsetProperty = RegisterCoordinate(nameof(HorizontalOffset));
    public static readonly DependencyProperty HorizontalScrollPaddingProperty = RegisterCoordinate(nameof(HorizontalScrollPadding));
    public double HorizontalScrollPadding { get => (double)GetValue(HorizontalScrollPaddingProperty); set => SetValue(HorizontalScrollPaddingProperty, value); }
    private static readonly DependencyPropertyKey IsMenuOwnerActivePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsMenuOwnerActive), typeof(bool), typeof(OHsControlPointLane), new PropertyMetadata(false));
    public static readonly DependencyProperty IsMenuOwnerActiveProperty = IsMenuOwnerActivePropertyKey.DependencyProperty;
    public ControlPointsSnapshot? State { get => (ControlPointsSnapshot?)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public double DurationMilliseconds { get => (double)GetValue(DurationMillisecondsProperty); set => SetValue(DurationMillisecondsProperty, value); }
    public double ContentWidth { get => (double)GetValue(ContentWidthProperty); set => SetValue(ContentWidthProperty, value); }
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double HorizontalOffset { get => (double)GetValue(HorizontalOffsetProperty); set => SetValue(HorizontalOffsetProperty, value); }
    public bool IsMenuOwnerActive => (bool)GetValue(IsMenuOwnerActiveProperty);
    private OHsControlPointLanePanel? panel;
    private string? measuredCulture;
    public event Action<double>? HorizontalOffsetRequested;
    internal int RealizedPointCount => EnsurePanel()?.RealizedPointCount ?? 0;
    internal int RecycledPointCount => EnsurePanel()?.RecycledPointCount ?? 0;

    public OHsControlPointLane()
    {
        InitializeComponent();
        UpdatePlaybackMarker();
        PointItems.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(OnZoneClick));
        Unloaded += (_, _) => ResetInteraction();
        SizeChanged += (_, _) => { CancelVolumeDrag(); CancelPointDrag(); };
        PointItems.LayoutUpdated += (_, _) => UpdateEditorViewport();
        IsEnabledChanged += (_, _) => { SetValue(IsMenuOwnerActivePropertyKey, State?.CanEdit == true && IsEnabled); if (!IsEnabled) ResetInteraction(); };
    }
    private static DependencyProperty RegisterCoordinate(string name) => DependencyProperty.Register(name, typeof(double), typeof(OHsControlPointLane),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, e) =>
        {
            var lane = (OHsControlPointLane)d;
            lane.CancelVolumeDrag();
            lane.CancelPointDrag();
            lane.SyncPanel();
            lane.UpdatePlaybackMarker();
            if (e.Property == ViewportWidthProperty) lane.panel?.InvalidateGeometry();
            lane.UpdateEditorViewport();
        }));

    private void OnLaneViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var border = (Border)sender;
        border.Clip = new RectangleGeometry(new Rect(e.NewSize), border.CornerRadius.TopLeft, border.CornerRadius.TopLeft);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var lane = (OHsControlPointLane)d;
        lane.RefreshEditor(e.OldValue as ControlPointsSnapshot);
        lane.RefreshInteraction(e.OldValue as ControlPointsSnapshot);
        lane.SetValue(IsMenuOwnerActivePropertyKey, lane.State?.CanEdit == true && lane.IsEnabled);
        var points = lane.State?.Points ?? [];
        if (lane.PointItems.ItemsSource is not IReadOnlyList<ControlPointSnapshot> previous || !previous.SequenceEqual(points))
            lane.PointItems.ItemsSource = points;
        lane.panel?.InvalidateMeasure();
        lane.UpdateSaveStatus();
    }

    private OHsControlPointLanePanel? EnsurePanel()
    {
        PointItems.ApplyTemplate();
        panel ??= FindChild<OHsControlPointLanePanel>(PointItems);
        if (panel is not null) SyncPanel();
        return panel;
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

    private OHsControlPointZone? FindRealizedTag(Guid id)
    {
        var index = State?.Points.ToList().FindIndex(p => p.Id == id) ?? -1;
        return index >= 0 && PointItems.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container
            ? FindChild<OHsControlPointZone>(container) : null;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        CancelVolumeDrag();
        CancelPointDrag();
        panel?.InvalidateGeometry();
    }

    public OHsControlPointZone? BringPointIntoView(Guid id)
    {
        if (EnsurePanel() is not { } current || GetExtents().FirstOrDefault(p => p.Id == id) is not { } extent) return null;
        current.PinnedPointId = id;
        var reveal = Math.Min(40, extent.Width);
        var offset = extent.Left + extent.Width <= HorizontalOffset ? extent.Left + extent.Width - reveal :
            extent.Left >= HorizontalOffset + ViewportWidth ? extent.Left - ViewportWidth + reveal : HorizontalOffset;
        var bounds = OHsControlPointLayoutBounds();
        var requested = Math.Clamp(offset, bounds.Minimum, bounds.Maximum);
        if (requested != HorizontalOffset) HorizontalOffsetRequested?.Invoke(requested);
        current.InvalidateMeasure(); PointItems.UpdateLayout();
        var index = State!.Points.ToList().FindIndex(p => p.Id == id);
        return PointItems.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container ? FindChild<OHsControlPointZone>(container) : null;
    }

    internal OHsControlPointZone? MovePointFocus(Guid currentId, bool forward)
    {
        if (State is null || KeyboardNavigationPolicy.GetSuppressTabNavigation(this)) return null;
        var index = State.Points.ToList().FindIndex(p => p.Id == currentId);
        var next = index + (forward ? 1 : -1);
        return index >= 0 && next >= 0 && next < State.Points.Count ? BringPointIntoView(State.Points[next].Id) : null;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Tab && Keyboard.FocusedElement is OHsControlPointZone { Point: { } point } &&
            MovePointFocus(point.Id, (Keyboard.Modifiers & ModifierKeys.Shift) == 0) is { } next)
        { next.Focus(); e.Handled = true; }
        if (HandlePointKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers, Keyboard.FocusedElement as DependencyObject)) e.Handled = true;
        base.OnPreviewKeyDown(e);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (!IsEditorOpen && dragPointId is null && volumeDragPointId is null && FindZone(e.NewFocus as DependencyObject) is null && panel is not null)
        { panel.PinnedPointId = null; panel.InvalidateMeasure(); }
    }

    internal static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T result) return result;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindChild<T>(VisualTreeHelper.GetChild(root, index)) is { } child) return child;
        return null;
    }
}
