using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.ComponentModel;
using System.Collections.Specialized;
using Hitbench.ViewModels.Shell;
using Hitbench.Resources.CustomControls;
using Track = Hitbench.Core.Entities.Track;

namespace Hitbench.Views.Pages;
public partial class OHsWorkstationTrackList : UserControl
{
    public static readonly DependencyProperty TrackSideColumnWidthProperty = DependencyProperty.Register(
        nameof(TrackSideColumnWidth), typeof(GridLength), typeof(OHsWorkstationTrackList), new PropertyMetadata(new GridLength(180)));
    public GridLength TrackSideColumnWidth { get => (GridLength)GetValue(TrackSideColumnWidthProperty); set => SetValue(TrackSideColumnWidthProperty, value); }

    private WorkstationPageVM viewModel = null!;
    private bool deactivationStarted;
    public bool IsVolumeOverlayOpen { get; set; }
    public bool AreTracksArranged => TrackList.IsArrangeValid;
    public event EventHandler? ViewportChanged;
    public OHsWorkstationTrackList()
    {
        InitializeComponent();
        ((ObjectDataProvider)Resources["OHsWorkstationTrackListSource"]).ObjectInstance = this;
        InitializeNodeInteraction();
        Unloaded += (_, e) => { if (ReferenceEquals(e.OriginalSource, this)) CancelNodePointer(); };
        InitializeTrackReorder();
        DataContextChanged += OnViewModelChanged;
    }
    private void OnViewModelChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (viewModel is not null) { CancelNodePointer(); viewModel.Tracks.CollectionChanged -= OnTracksCollectionChanged; viewModel.PropertyChanged -= OnPresentationChanged; }
        viewModel = e.NewValue as WorkstationPageVM ?? null!;
        if (viewModel is not null && !deactivationStarted) { viewModel.Tracks.CollectionChanged += OnTracksCollectionChanged; viewModel.PropertyChanged += OnPresentationChanged; }
    }
    private void OnPresentationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsRegionSelectionActive && (!viewModel.IsSelectionMode || e.PropertyName == nameof(WorkstationPageVM.ZoomFactor))) CancelNodePointer();
        if (e.PropertyName == nameof(WorkstationPageVM.IsSampleConfigurationOpen) && viewModel.IsSampleConfigurationOpen) CancelNodePointer();
        if (e.PropertyName == nameof(WorkstationPageVM.IsSelectionMode) && !viewModel.IsSelectionMode) CancelTrackPointer();
        if (e.PropertyName == nameof(WorkstationPageVM.LoadPhase) && viewModel.IsLoading) CancelNodePointer();
        if (e.PropertyName == nameof(WorkstationPageVM.IsSelectionMode) && nodeSourceGrid is not null && pointerNode is null) CancelNodePointer();
        if (!viewModel.CanPlaceSamples || nodeDragging && !viewModel.IsNodeDragActive) CancelNodePointer();
        RefreshTrackDragPresentation();
    }
    public void Deactivate()
    {
        if (deactivationStarted) return;
        CancelNodePointer(); deactivationStarted = true;
        if (viewModel is not null) { viewModel.Tracks.CollectionChanged -= OnTracksCollectionChanged; viewModel.PropertyChanged -= OnPresentationChanged; }
        DataContextChanged -= OnViewModelChanged;
    }
    public bool RestoreTrackFocusBeforeCollapse(DependencyObject? focusedElement) =>
        !deactivationStarted && RealizedRows(TrackList).Any(row => row.HasExpandedControlFocus(focusedElement));
    public void RefreshViewport() { UpdateNodeAdornerGeometry(); RefreshNodeDragAfterScroll(); RefreshTrackDragPresentation(); }
    public static readonly DependencyProperty TimelineProperty = DependencyProperty.Register(nameof(Timeline), typeof(OHsWorkstationTimeline), typeof(OHsWorkstationTrackList), new PropertyMetadata(null));
    public OHsWorkstationTimeline? Timeline { get => (OHsWorkstationTimeline?)GetValue(TimelineProperty); set => SetValue(TimelineProperty, value); }
    public static readonly DependencyProperty TimelineContentWidthProperty = DependencyProperty.Register(nameof(TimelineContentWidth), typeof(double), typeof(OHsWorkstationTrackList), new PropertyMetadata(0d));
    public double TimelineContentWidth { get => (double)GetValue(TimelineContentWidthProperty); set => SetValue(TimelineContentWidthProperty, value); }
    public static readonly DependencyProperty TimelineViewportWidthProperty = DependencyProperty.Register(nameof(TimelineViewportWidth), typeof(double), typeof(OHsWorkstationTrackList), new PropertyMetadata(0d));
    public double TimelineViewportWidth { get => (double)GetValue(TimelineViewportWidthProperty); set => SetValue(TimelineViewportWidthProperty, value); }
    public static readonly DependencyProperty TimelineHorizontalOffsetProperty = DependencyProperty.Register(nameof(TimelineHorizontalOffset), typeof(double), typeof(OHsWorkstationTrackList), new PropertyMetadata(0d));
    public double TimelineHorizontalOffset { get => (double)GetValue(TimelineHorizontalOffsetProperty); set => SetValue(TimelineHorizontalOffsetProperty, value); }
    private static readonly DependencyPropertyKey TrackScrollBarGutterPropertyKey = DependencyProperty.RegisterReadOnly(nameof(TrackScrollBarGutter), typeof(double), typeof(OHsWorkstationTrackList), new PropertyMetadata(0d));
    public static readonly DependencyProperty TrackScrollBarGutterProperty = TrackScrollBarGutterPropertyKey.DependencyProperty;
    public double TrackScrollBarGutter { get => (double)GetValue(TrackScrollBarGutterProperty); private set => SetValue(TrackScrollBarGutterPropertyKey, value); }
    private static DependencyObject? GetInputParent(DependencyObject current) => current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
    private static bool IsWithin(DependencyObject? origin, DependencyObject owner)
    {
        for (var current = origin; current is not null; current = GetInputParent(current)) if (ReferenceEquals(current, owner)) return true;
        return false;
    }
    private static T? FindAncestor<T>(DependencyObject? origin) where T : DependencyObject
    {
        for (var current = origin; current is not null; current = GetInputParent(current)) if (current is T value) return value;
        return null;
    }
    private static T? FindVisualChild<T>(DependencyObject root, string? name = null) where T : DependencyObject
    {
        if (root is T match && (name is null || root is FrameworkElement element && element.Name == name)) return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindVisualChild<T>(VisualTreeHelper.GetChild(root, index), name);
            if (found is not null) return found;
        }
        return null;
    }

    private void OnTracksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        trackOrderRevision++;
        if (IsRegionSelectionActive) CancelNodePointer();
        foreach (var row in RealizedRows(TrackList)) row.RefreshPresentation();
        CancelTrackPointer();
        if (e.Action == NotifyCollectionChangedAction.Add && !deactivationStarted && viewModel.IsInteractionReady && e.NewItems is { Count: > 0 })
        {
            var generation = viewModel.LoadGeneration;
            var added = e.NewItems[e.NewItems.Count - 1];
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (deactivationStarted || viewModel.LoadGeneration != generation || !TrackList.Items.Contains(added)) return;
                TrackList.UpdateLayout();
                FindVisualChild<ScrollViewer>(TrackList)?.ScrollToBottom();
            });
            return;
        }
        if (e.Action != NotifyCollectionChangedAction.Remove || e.OldItems is null || deactivationStarted)
            return;
        var focus = Keyboard.FocusedElement as DependencyObject;
        for (var current = focus; current is not null; current = GetInputParent(current))
        {
            if (current is FrameworkElement element && e.OldItems.Contains(element.DataContext))
            {
                var index = Math.Clamp(e.OldStartingIndex, 0, Math.Max(0, viewModel.Tracks.Count - 1));
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    if (deactivationStarted) return;
                    if (FindRemovalFocusTarget(index) is not UIElement target || !target.Focus())
                        Timeline?.FocusTimeline();
                });
                break;
            }
        }
    }

    public IInputElement? FindRemovalFocusTarget(int index)
    {
        TrackList.UpdateLayout();
        var container = TrackList.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
        return container is null ? Timeline?.FocusTarget
            : FindVisualChild<OHsWorkstationTrackRow>(container)?.GetRemovalFocusTarget() ?? Timeline?.FocusTarget;
    }

    public void PrepareTrackDrawings()
    {
        foreach (var row in RealizedRows(TrackList)) row.PrepareDrawing();
    }
    private static IEnumerable<OHsWorkstationTrackRow> RealizedRows(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is OHsWorkstationTrackRow row) yield return row;
            else foreach (var nested in RealizedRows(child)) yield return nested;
        }
    }
    private static readonly DependencyPropertyKey TrackViewportWidthPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(TrackViewportWidth), typeof(double), typeof(OHsWorkstationTrackList), new PropertyMetadata(0d));
    public static readonly DependencyProperty TrackViewportWidthProperty = TrackViewportWidthPropertyKey.DependencyProperty;
    public double TrackViewportWidth => (double)GetValue(TrackViewportWidthProperty);

    private void OnTrackListViewportChanged(object sender, ScrollChangedEventArgs e)
    {
        if (deactivationStarted || sender is not ScrollViewer scroll || !ReferenceEquals(e.OriginalSource, sender)) return;
        var width = Math.Max(0, scroll.ViewportWidth - TrackSideColumnWidth.Value - 12);
        if (double.IsFinite(width)) SetValue(TrackViewportWidthPropertyKey, width);
        var gutter = Math.Max(0, scroll.ActualWidth - scroll.ViewportWidth);
        if (double.IsFinite(gutter) && TrackScrollBarGutter != gutter)
        {
            TrackScrollBarGutter = gutter;
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }
        UpdateNodeAdornerGeometry();
        RefreshTrackDragPresentation();
    }
}
