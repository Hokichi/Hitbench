using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using osuHitsounder.Core.Entities;
using Track = osuHitsounder.Core.Entities.Track;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Views.Pages;

public partial class WorkstationPage
{
    private BeatGridView? nodeSourceGrid;
    private Sample? pointerNode;
    private Point pointerStart;
    private bool nodeDragging;
    private bool nodeTargetValid;
    private AdornerLayer? nodeLayer;
    private OHsSampleNodeDragAdorner? nodeAdorner;
    internal Task NodeInputCompletion { get; private set; } = Task.CompletedTask;

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
        var grid = FindAncestor<BeatGridView>(e.OriginalSource as DependencyObject);
        if (grid is null || !BeginNodePointer(grid, e.GetPosition(grid), e.GetPosition(this))) return;
        grid.Focus();
        if (!CaptureMouse()) CancelNodePointer();
        e.Handled = true;
    }
    internal bool BeginNodePointer(BeatGridView grid, Point gridPoint, Point pagePoint)
    {
        CancelNodePointer();
        if (!double.IsFinite(pagePoint.X) || !double.IsFinite(pagePoint.Y) || IsVolumeOverlayOpen || deactivationStarted || grid.DataContext is not Track track || grid.FindSampleAt(gridPoint) is not { } sample ||
            !viewModel.SelectNode(track, sample, false)) return false;
        nodeSourceGrid = grid; pointerNode = sample; pointerStart = pagePoint;
        grid.PressedSample = sample; grid.SuppressPlacementFeedback = true;
        grid.DataContextChanged += OnNodeGridRecycled;
        grid.Unloaded += OnNodeGridUnloaded;
        return true;
    }
    private void OnNodeOwnerDeactivated(object? sender, EventArgs e) => CancelNodePointer();
    private void OnNodeGridUnloaded(object sender, RoutedEventArgs e) => CancelNodePointer();
    private void OnNodeGridRecycled(object sender, DependencyPropertyChangedEventArgs e) => CancelNodePointer();
    private void OnNodePointerMove(object sender, MouseEventArgs e)
    {
        if (pointerNode is null) return;
        UpdateNodePointerAt(e.GetPosition(this));
        e.Handled = true;
    }
    private void UpdateNodePointerAt(Point pagePoint)
    {
        var grid = FindAncestor<BeatGridView>(InputHitTest(pagePoint) as DependencyObject);
        UpdateNodePointer(pagePoint, grid, grid is null ? default : TranslatePoint(pagePoint, grid));
    }
    internal void UpdateNodePointer(Point pagePoint, BeatGridView? targetGrid, Point targetPoint)
    {
        if (!double.IsFinite(pagePoint.X) || !double.IsFinite(pagePoint.Y) || pointerNode is null || nodeSourceGrid?.DataContext is not Track source || !viewModel.CanPlaceSamples || IsVolumeOverlayOpen) { CancelNodePointer(); return; }
        if (!nodeDragging)
        {
            if (Math.Abs(pagePoint.X - pointerStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(pagePoint.Y - pointerStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (!viewModel.BeginNodeDrag(source, pointerNode)) { CancelNodePointer(); return; }
            nodeLayer = AdornerLayer.GetAdornerLayer(TrackList);
            if (nodeLayer is null) { CancelNodePointer(); return; }
            nodeAdorner = new OHsSampleNodeDragAdorner(TrackList) { IsHitTestVisible = false, NodeHeight = nodeSourceGrid.ActualHeight, NodeWidth = nodeSourceGrid.GetDisplayedSampleBounds(pointerNode).Width };
            nodeLayer.Add(nodeAdorner); nodeDragging = true;
        }
        nodeAdorner!.CursorPosition = TranslatePoint(pagePoint, TrackList);
        nodeTargetValid = targetGrid is { DataContext: Track } && targetGrid.CanPlaceSamples &&
            targetPoint.X >= 0 && targetPoint.X < Math.Min(targetGrid.ActualWidth, targetGrid.ViewportWidth) && targetPoint.Y >= 0 && targetPoint.Y < targetGrid.ActualHeight &&
            targetGrid.ContentWidth > 0 && double.IsFinite(targetGrid.ContentWidth) && viewModel.Tracks.Contains((Track)targetGrid.DataContext) && double.IsFinite(targetPoint.X) && double.IsFinite(targetPoint.Y);
        if (!nodeTargetValid) return;
        var time = (targetPoint.X + targetGrid!.HorizontalOffset) / targetGrid.ContentWidth * targetGrid.Duration.TotalMilliseconds;
        if (!double.IsFinite(time) || time < 0 || time >= targetGrid.Duration.TotalMilliseconds) { nodeTargetValid = false; return; }
        viewModel.PreviewNodeDrag(time - pointerNode.Timestamp, (Track)targetGrid.DataContext);
        if (viewModel.NodePreviewTimestamps.TryGetValue(pointerNode, out var snapped)) targetGrid.SetActivePosition(TimeSpan.FromMilliseconds(snapped));
    }
    private void OnNodePointerUp(object sender, MouseButtonEventArgs e)
    {
        if (pointerNode is null) return;
        UpdateNodePointerAt(e.GetPosition(this));
        NodeInputCompletion = ObserveNodeInputAsync(CompleteNodePointerAsync()); e.Handled = true;
    }
    internal async Task<bool> CompleteNodePointerAsync()
    {
        var commit = nodeDragging && nodeTargetValid;
        DetachNodePointer();
        if (!commit) { viewModel.CancelNodeDrag(); return false; }
        return await viewModel.CommitNodeDragAsync();
    }
    private void DetachNodePointer()
    {
        if (nodeSourceGrid is not null) { nodeSourceGrid.DataContextChanged -= OnNodeGridRecycled; nodeSourceGrid.Unloaded -= OnNodeGridUnloaded; nodeSourceGrid.PressedSample = null; nodeSourceGrid.SuppressPlacementFeedback = false; }
        if (nodeAdorner is not null) nodeLayer?.Remove(nodeAdorner);
        pointerNode = null; nodeSourceGrid = null; nodeAdorner = null; nodeLayer = null; nodeDragging = false; nodeTargetValid = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
    internal void CancelNodePointer() { DetachNodePointer(); viewModel.CancelNodeDrag(); }
    private void OnNodeCaptureLost(object sender, MouseEventArgs e) { if (pointerNode is not null) CancelNodePointer(); }
    private async Task ObserveNodeInputAsync(Task operation)
    {
        try { await operation; }
        catch (OperationCanceledException) { }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Node input failed: {0}", error); }
    }
    private bool HandleNodeKey(Key key, ModifierKeys modifiers, bool isRepeat, DependencyObject? origin)
    {
        if (key == Key.Escape && pointerNode is not null) { CancelNodePointer(); return true; }
        if (FindAncestor<TextBoxBase>(origin) is not null || FindAncestor<PasswordBox>(origin) is not null || FindAncestor<ComboBox>(origin)?.IsEditable == true) return false;
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
