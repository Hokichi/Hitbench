using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Core.Entities;
using Track = osuHitsounder.Core.Entities.Track;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Views.Pages;

public partial class WorkstationPage
{
    private BeatGridView? nodeSourceGrid;
    private BeatGridView? nodeTargetGrid;
    private Sample? pointerNode;
    private Point pointerStart;
    private bool nodeDragging;
    private bool nodeTargetValid;
    private AdornerLayer? nodeLayer;
    private OHsSampleNodeDragAdorner? nodeAdorner;
    private (Sample Sample, int Timestamp, int TrackIndex)[] nodeDragGroup = [];
    internal Task NodeInputCompletion { get; private set; } = Task.CompletedTask;
    private ModifierKeys pointerModifiers;
    private BeatInterval? pointerInterval;
    private bool pointerMoved;
    private Point pointerLast;
    private OHsSelectionRegionAdorner? regionAdorner;
    internal bool IsRegionSelectionActive => regionAdorner is not null;

    private void InitializeNodeInteraction()
    {
        PreviewMouseLeftButtonDown += OnNodePointerDown;
        PreviewMouseMove += OnNodePointerMove;
        PreviewMouseLeftButtonUp += OnNodePointerUp;
        LostMouseCapture += OnNodeCaptureLost;
        PreviewGotKeyboardFocus += OnTrackFocused;
    }
    private void OnTrackFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.NewFocus as DependencyObject);
        if (item?.DataContext is Track track) viewModel.SelectedTrack = track;
    }
    private void OnSampleSelectionRequested(object sender, SampleSelectionRequestedEventArgs e)
    {
        if (sender is BeatGridView { DataContext: Track track }) viewModel.SelectNode(track, e.Sample, false);
        e.Handled = true;
    }
    private void OnNodePointerDown(object sender, MouseButtonEventArgs e)
    {
        var origin = e.OriginalSource as DependencyObject;
        if (IsWithin(origin, ToggleTracksCollapsedButton)) return;
        if (!deactivationStarted && !IsWithin(origin, TracksCard)) viewModel.SelectedTrack = null;
        var grid = FindAncestor<BeatGridView>(origin);
        if (!BeginNodePointer(grid, grid is null ? default : e.GetPosition(grid), e.GetPosition(this), Keyboard.Modifiers)) return;
        grid!.Focus();
        if (nodeSourceGrid is not null && !CaptureMouse()) CancelNodePointer();
        e.Handled = true;
    }
    internal bool BeginNodePointer(BeatGridView? grid, Point gridPoint, Point pagePoint, ModifierKeys modifiers = ModifierKeys.None)
    {
        CancelNodePointer();
        if (!double.IsFinite(pagePoint.X) || !double.IsFinite(pagePoint.Y) || deactivationStarted) return false;
        if (grid is null) { viewModel.ClearNodeSelection(); return false; }
        if (!double.IsFinite(gridPoint.X) || !double.IsFinite(gridPoint.Y)) return false;
        var sample = grid.FindSampleAt(gridPoint);
        if (IsVolumeOverlayOpen || !viewModel.CanPlaceSamples || !grid.CanPlaceSamples || grid.DataContext is not Track track ||
            !viewModel.CanEditTrackGrid(track) || gridPoint.X < 0 || gridPoint.X >= Math.Min(grid.ActualWidth, grid.ViewportWidth) ||
            gridPoint.Y < 0 || gridPoint.Y >= grid.ActualHeight) return false;
        if (sample is not null && !modifiers.HasFlag(ModifierKeys.Control))
        {
            if (!viewModel.SelectedSamples.Contains(sample))
            {
                if (!viewModel.SelectNode(track, sample, false)) return false;
            }
            else { viewModel.SelectedTrack = track; viewModel.SetNodeSelection(viewModel.SelectedSamples); }
        }
        grid.UpdatePointerPosition(gridPoint.X);
        pointerInterval = grid.ActiveInterval;
        nodeSourceGrid = grid; pointerNode = sample; pointerStart = pointerLast = pagePoint;
        pointerModifiers = modifiers; pointerMoved = false;
        grid.PressedSample = modifiers.HasFlag(ModifierKeys.Control) ? null : sample; grid.SuppressPlacementFeedback = true;
        grid.DataContextChanged += OnNodeGridRecycled;
        grid.Unloaded += OnNodeGridUnloaded;
        return true;
    }
    private void OnNodeOwnerDeactivated(object? sender, EventArgs e) => CancelNodePointer();
    private void OnNodeGridUnloaded(object sender, RoutedEventArgs e) => CancelNodePointer();
    private void OnNodeGridRecycled(object sender, DependencyPropertyChangedEventArgs e) => CancelNodePointer();
    private void OnNodePointerMove(object sender, MouseEventArgs e)
    {
        if (nodeSourceGrid is null) return;
        UpdateNodePointerAt(e.GetPosition(this));
        e.Handled = true;
    }
    private void UpdateNodePointerAt(Point pagePoint)
    {
        var grid = FindAncestor<BeatGridView>(InputHitTest(pagePoint) as DependencyObject);
        UpdateNodePointer(pagePoint, grid, grid is null ? default : TranslatePoint(pagePoint, grid), Keyboard.Modifiers);
    }
    internal void UpdateNodePointer(Point pagePoint, BeatGridView? targetGrid, Point targetPoint, ModifierKeys modifiers = ModifierKeys.None)
    {
        if (!double.IsFinite(pagePoint.X) || !double.IsFinite(pagePoint.Y) || nodeSourceGrid?.DataContext is not Track source ||
            !viewModel.CanPlaceSamples || !viewModel.CanEditTrackGrid(source) || IsVolumeOverlayOpen) { CancelNodePointer(); return; }
        pointerLast = pagePoint;
        if (!pointerModifiers.HasFlag(ModifierKeys.Control) && modifiers.HasFlag(ModifierKeys.Control)) { CancelNodePointer(); return; }
        var pastThreshold = Math.Abs(pagePoint.X - pointerStart.X) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(pagePoint.Y - pointerStart.Y) >= SystemParameters.MinimumVerticalDragDistance;
        if (pointerModifiers.HasFlag(ModifierKeys.Control)) { pointerMoved |= pastThreshold; return; }
        if (pointerNode is null)
        {
            if (!pointerMoved && !pastThreshold) return;
            if (!viewModel.IsSelectionMode) { CancelNodePointer(); return; }
            pointerMoved = true;
            if (regionAdorner is null)
            {
                nodeLayer = AdornerLayer.GetAdornerLayer(TrackList);
                if (nodeLayer is null) { CancelNodePointer(); return; }
                regionAdorner = new OHsSelectionRegionAdorner(TrackList);
                nodeLayer.Add(regionAdorner);
                TrackList.LayoutUpdated += OnRegionLayoutUpdated;
            }
            UpdateRegionSelection(pagePoint);
            return;
        }
        if (!nodeDragging)
        {
            if (!pastThreshold) return;
            pointerMoved = true;
            if (!viewModel.BeginNodeDrag(source, pointerNode)) { CancelNodePointer(); return; }
            nodeLayer = AdornerLayer.GetAdornerLayer(TrackList);
            if (nodeLayer is null) { CancelNodePointer(); return; }
            nodeDragGroup = viewModel.Tracks.SelectMany((track, index) => track.Samples
                .Where(viewModel.SelectedSamples.Contains).Select(sample => (sample, sample.Timestamp, index))).ToArray();
            nodeAdorner = new OHsSampleNodeDragAdorner(TrackList);
            nodeLayer.Add(nodeAdorner); nodeDragging = true;
            TrackList.LayoutUpdated += OnNodeDragLayoutUpdated;
        }
        nodeAdorner!.CursorPosition = TranslatePoint(pagePoint, TrackList);
        UpdateNodeAdornerGeometry();
        nodeTargetValid = targetGrid is { DataContext: Track } && targetGrid.CanPlaceSamples &&
            targetPoint.X >= 0 && targetPoint.X < Math.Min(targetGrid.ActualWidth, targetGrid.ViewportWidth) && targetPoint.Y >= 0 && targetPoint.Y < targetGrid.ActualHeight &&
            targetGrid.ContentWidth > 0 && double.IsFinite(targetGrid.ContentWidth) && viewModel.Tracks.Contains((Track)targetGrid.DataContext) && double.IsFinite(targetPoint.X) && double.IsFinite(targetPoint.Y);
        if (!nodeTargetValid) { viewModel.ClearNodeDragPreview(); SetNodeDragTarget(null); return; }
        var time = (targetPoint.X + targetGrid!.HorizontalOffset) / targetGrid.ContentWidth * targetGrid.Duration.TotalMilliseconds;
        if (!double.IsFinite(time) || time < 0 || time >= targetGrid.Duration.TotalMilliseconds) { nodeTargetValid = false; viewModel.ClearNodeDragPreview(); SetNodeDragTarget(null); return; }
        nodeTargetValid = viewModel.PreviewNodeDrag(time - pointerNode.Timestamp, (Track)targetGrid.DataContext);
        if (nodeTargetValid && pointerNode is not null && viewModel.NodePreviewTimestamps.ContainsKey(pointerNode)) SetNodeDragTarget(targetGrid);
        else { nodeTargetValid = false; SetNodeDragTarget(null); }
    }

    private void SetNodeDragTarget(BeatGridView? grid)
    {
        if (!ReferenceEquals(nodeTargetGrid, grid))
        {
            if (nodeTargetGrid is not null)
            {
                nodeTargetGrid.DragPreviewTimestamps = null;
                nodeTargetGrid.UpdatePointerPosition(null);
                if (!ReferenceEquals(nodeTargetGrid, nodeSourceGrid))
                {
                    nodeTargetGrid.DataContextChanged -= OnNodeGridRecycled;
                    nodeTargetGrid.Unloaded -= OnNodeGridUnloaded;
                }
            }
            nodeTargetGrid = grid;
            if (grid is not null && !ReferenceEquals(grid, nodeSourceGrid))
            {
                grid.DataContextChanged += OnNodeGridRecycled;
                grid.Unloaded += OnNodeGridUnloaded;
            }
        }
        UpdateNodeDragPlaceholders();
    }

    private void UpdateNodeDragPlaceholders()
    {
        foreach (var grid in RealizedTrackGrids(TrackList))
        {
            var timestamps = nodeDragging && nodeTargetValid && nodeTargetGrid is not null && viewModel.IsNodeDragActive &&
                grid.IsEnabled && grid.CanPlaceSamples && grid.DataContext is Track track && viewModel.NodePreviewByTrack.TryGetValue(track, out var preview)
                ? preview : null;
            if (grid.DragPreviewTimestamps is null ? timestamps is null : timestamps is not null && grid.DragPreviewTimestamps.SequenceEqual(timestamps)) continue;
            grid.DragPreviewTimestamps = timestamps;
        }
    }

    private void OnNodeDragLayoutUpdated(object? sender, EventArgs e)
    {
        UpdateNodeAdornerGeometry();
        UpdateNodeDragPlaceholders();
    }

    private void OnRegionLayoutUpdated(object? sender, EventArgs e) => UpdateRegionSelection(pointerLast);

    private Rect GetTimelineViewportBounds()
    {
        if (nodeSourceGrid is null) return Rect.Empty;
        var presenter = FindVisualChild<ScrollContentPresenter>(TrackList);
        if (presenter is null) return Rect.Empty;
        var viewport = presenter.TransformToVisual(TrackList).TransformBounds(new Rect(presenter.RenderSize));
        var gridBounds = nodeSourceGrid.TransformToVisual(TrackList).TransformBounds(new Rect(0, 0,
            Math.Max(0, Math.Min(nodeSourceGrid.ActualWidth, nodeSourceGrid.ViewportWidth)), nodeSourceGrid.ActualHeight));
        var timeline = new Rect(gridBounds.Left, viewport.Top, gridBounds.Width, viewport.Height);
        timeline.Intersect(viewport);
        return timeline;
    }

    private void UpdateRegionSelection(Point pagePoint)
    {
        if (regionAdorner is null) return;
        if (nodeSourceGrid?.DataContext is not Track source || !viewModel.IsSelectionMode || !viewModel.CanPlaceSamples || !viewModel.CanEditTrackGrid(source))
        { CancelNodePointer(); return; }
        var clip = GetTimelineViewportBounds();
        var bounds = new Rect(TranslatePoint(pointerStart, TrackList), TranslatePoint(pagePoint, TrackList));
        bounds.Intersect(clip);
        regionAdorner.ClipBounds = clip;
        regionAdorner.SelectionBounds = bounds;
        var selected = new List<Sample>();
        if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
            foreach (var grid in RealizedTrackGrids(TrackList))
            {
                if (!grid.IsEnabled || grid.DataContext is not Track track || !viewModel.CanEditTrackGrid(track)) continue;
                grid.GetBindingExpression(BeatGridView.ContentWidthProperty)?.UpdateTarget();
                grid.GetBindingExpression(BeatGridView.HorizontalOffsetProperty)?.UpdateTarget();
                var local = TrackList.TransformToVisual(grid).TransformBounds(bounds);
                selected.AddRange(grid.GetSamplesIntersecting(local));
            }
        viewModel.SetNodeSelection(selected);
    }

    private static IEnumerable<BeatGridView> RealizedTrackGrids(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is BeatGridView grid) yield return grid;
            else foreach (var nested in RealizedTrackGrids(child)) yield return nested;
        }
    }

    private void UpdateNodeAdornerGeometry()
    {
        if (IsRegionSelectionActive) UpdateRegionSelection(pointerLast);
        if (nodeAdorner is null || pointerNode is null || nodeSourceGrid is not { } grid || nodeDragGroup.Length == 0) return;
        grid.GetBindingExpression(BeatGridView.ZoomFactorProperty)?.UpdateTarget();
        var anchor = nodeDragGroup.Single(node => ReferenceEquals(node.Sample, pointerNode));
        var anchorBounds = grid.GetSampleBounds(anchor.Timestamp);
        if (anchorBounds.IsEmpty) return;
        var anchorRect = grid.TransformToVisual(TrackList).TransformBounds(anchorBounds);
        var center = new Point(anchorRect.X + anchorRect.Width / 2, anchorRect.Y + anchorRect.Height / 2);
        var container = FindAncestor<ListBoxItem>(grid);
        var rowPitch = container is null ? grid.ActualHeight : container.ActualHeight + container.Margin.Top + container.Margin.Bottom;
        var next = new Rect[nodeDragGroup.Length];
        for (var i = 0; i < nodeDragGroup.Length; i++)
        {
            var node = nodeDragGroup[i];
            var item = TrackList.ItemContainerGenerator.ContainerFromIndex(node.TrackIndex) as ListBoxItem;
            var realized = item is null ? null : FindVisualChild<BeatGridView>(item);
            var owner = realized ?? grid;
            var bounds = owner.GetSampleBounds(node.Timestamp);
            if (bounds.IsEmpty) return;
            var rect = owner.TransformToVisual(TrackList).TransformBounds(bounds);
            rect.Offset(-center.X, -center.Y + (realized is null ? (node.TrackIndex - anchor.TrackIndex) * rowPitch : 0));
            next[i] = rect;
        }
        if (!nodeAdorner.NodeBounds.SequenceEqual(next)) nodeAdorner.NodeBounds = Array.AsReadOnly(next);
    }
    private void OnNodePointerUp(object sender, MouseButtonEventArgs e)
    {
        if (nodeSourceGrid is null) return;
        UpdateNodePointerAt(e.GetPosition(this));
        NodeInputCompletion = ObserveNodeInputAsync(CompleteNodePointerAsync()); e.Handled = true;
    }
    internal async Task<bool> CompleteNodePointerAsync()
    {
        var commit = nodeDragging && nodeTargetValid;
        var click = nodeSourceGrid is not null && !pointerMoved;
        var grid = nodeSourceGrid; var sample = pointerNode; var interval = pointerInterval;
        var toggle = pointerModifiers.HasFlag(ModifierKeys.Control);
        DetachNodePointer();
        if (commit) return await viewModel.CommitNodeDragAsync();
        viewModel.CancelNodeDrag();
        if (!click || !viewModel.CanPlaceSamples || IsVolumeOverlayOpen || grid?.DataContext is not Track track || !viewModel.CanEditTrackGrid(track)) return false;
        if (toggle) return sample is not null && viewModel.SelectNode(track, sample, true);
        if (sample is not null) return false;
        viewModel.ClearNodeSelection();
        if (interval is null) return false;
        grid.SetActivePosition(TimeSpan.FromMilliseconds(interval.Value.StartMilliseconds));
        return grid.FindActiveSample() is null && grid.RequestSamplePlacement();
    }
    private void DetachNodePointer()
    {
        TrackList.LayoutUpdated -= OnNodeDragLayoutUpdated;
        TrackList.LayoutUpdated -= OnRegionLayoutUpdated;
        SetNodeDragTarget(null);
        if (nodeSourceGrid is not null) { nodeSourceGrid.DataContextChanged -= OnNodeGridRecycled; nodeSourceGrid.Unloaded -= OnNodeGridUnloaded; nodeSourceGrid.PressedSample = null; nodeSourceGrid.SuppressPlacementFeedback = false; }
        if (nodeAdorner is not null) nodeLayer?.Remove(nodeAdorner);
        if (regionAdorner is not null) nodeLayer?.Remove(regionAdorner);
        regionAdorner = null; pointerInterval = null; pointerModifiers = ModifierKeys.None; pointerMoved = false;
        pointerNode = null; nodeSourceGrid = null; nodeAdorner = null; nodeLayer = null; nodeDragGroup = []; nodeDragging = false; nodeTargetValid = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
    internal void CancelNodePointer() { DetachNodePointer(); viewModel.CancelNodeDrag(); }
    private void OnNodeCaptureLost(object sender, MouseEventArgs e) { if (nodeSourceGrid is not null) CancelNodePointer(); }
    private async Task ObserveNodeInputAsync(Task operation)
    {
        try { await operation; }
        catch (OperationCanceledException) { }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Node input failed: {0}", error); }
    }
    private bool HandleNodeKey(Key key, ModifierKeys modifiers, bool isRepeat, DependencyObject? origin)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl && nodeSourceGrid is not null && !pointerModifiers.HasFlag(ModifierKeys.Control))
        { CancelNodePointer(); return true; }
        if (key == Key.Escape && nodeSourceGrid is not null) { CancelNodePointer(); return true; }
        if (FindAncestor<TextBoxBase>(origin) is not null || FindAncestor<PasswordBox>(origin) is not null || FindAncestor<ComboBox>(origin)?.IsEditable == true) return false;
        if (key == Key.Escape && modifiers == ModifierKeys.None && viewModel.SelectedSamples.Count > 0)
        {
            if (!isRepeat) viewModel.ClearNodeSelection();
            return true;
        }
        if (key == Key.Enter && modifiers == ModifierKeys.Control && FindAncestor<BeatGridView>(origin) is { CanPlaceSamples: true, DataContext: Track track } grid)
        {
            if (!isRepeat && grid.FindActiveSample() is { } sample) viewModel.SelectNode(track, sample, true);
            return true;
        }
        System.Windows.Input.ICommand? command = modifiers == ModifierKeys.Control ? key switch
        { Key.C => viewModel.CopyNodesCommand, Key.V => viewModel.PasteNodesCommand, _ => null } :
            modifiers == ModifierKeys.None && key == Key.Delete ? viewModel.DeleteSelectedNodesCommand : null;
        if (command is not null)
        {
            if (!command.CanExecute(null)) return false;
            if (!isRepeat)
            {
                if (key != Key.C) CancelNodePointer();
                if (command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand asyncCommand) NodeInputCompletion = ObserveNodeInputAsync(asyncCommand.ExecuteAsync(null));
                else command.Execute(null);
            }
            return true;
        }
        if (modifiers != ModifierKeys.Alt || key is not (Key.Left or Key.Right or Key.Up or Key.Down) || !viewModel.CanCopyNodes) return false;
        if (!isRepeat)
        {
            CancelNodePointer();
            NodeInputCompletion = ObserveNodeInputAsync(key is Key.Left or Key.Right ? viewModel.MoveSelectedNodesAsync(key == Key.Right) : viewModel.TransferSelectedNodesAsync(key == Key.Down));
        }
        return true;
    }
}
