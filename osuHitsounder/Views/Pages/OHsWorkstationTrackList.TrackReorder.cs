using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.ComponentModel;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.Helpers;
using osuHitsounder.ViewModels.Shell;
using Track = osuHitsounder.Core.Entities.Track;

namespace osuHitsounder.Views.Pages;

public partial class OHsWorkstationTrackList
{
    private WorkstationPageVM? trackDragOwner;
    private ProjectData? trackDragProject;
    private int trackDragGeneration;
    private int trackOrderRevision;
    private Point trackPointerStart;
    private bool trackPointerCaptured;
    private Point trackPointerLast;
    private AdornerLayer? trackDragLayer;
    private OHsTrackDragAdorner? trackDragAdorner;
    private ImageSource? trackDragImage;
    private bool refreshingTrackDrag;
    private int trackWheelRemainder;
    private readonly HashSet<ListBoxItem> trackDragContainers = [];
    private (double Extent, string Name, bool Collapsed, bool Muted, bool Solo, int Volume, string? Sample, double Width, double Offset) trackImageState;
    private static readonly DependencyProperty TrackDragThemeBrushProperty = DependencyProperty.Register(
        "TrackDragThemeBrush", typeof(Brush), typeof(OHsWorkstationTrackList),
        new PropertyMetadata(null, (d, _) => ((OHsWorkstationTrackList)d).RefreshTrackDragPresentation(true)));
    internal bool IsTrackDragActive { get; private set; }
    internal Track? DraggedTrack { get; private set; }
    internal int? TrackInsertionSlot { get; private set; }
    internal Func<bool>? CaptureTrackPointer { get; set; }
    internal Action? ReleaseTrackPointer { get; set; }
    internal Func<OHsWorkstationTrackRow, ImageSource?>? CaptureTrackPreview { get; set; }

    private void InitializeTrackReorder()
    {
        PreviewMouseLeftButtonDown += OnTrackPointerDown;
        PreviewMouseMove += OnTrackPointerMove;
        // A release consumed elsewhere must still end our captured drag.
        AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnTrackPointerUp), true);
        LostMouseCapture += OnTrackPointerCaptureLost;
        MouseLeave += OnTrackPointerLeave;
        PreviewKeyDown += OnTrackDragKeyDown;
        Unloaded += OnTrackDragUnloaded;
        SetResourceReference(TrackDragThemeBrushProperty, "Brush.Background.Surface");
    }

    private void OnTrackPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (!e.Handled) BeginTrackPointer(e.OriginalSource as DependencyObject, e.GetPosition(this));
    }
    private void OnTrackPointerMove(object sender, MouseEventArgs e)
    {
        if (DraggedTrack is null) return;
        UpdateTrackPointer(e.GetPosition(this), e.LeftButton == MouseButtonState.Pressed);
        if (IsTrackDragActive) e.Handled = true;
    }
    private void OnTrackPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (IsTrackDragActive) e.Handled = true;
        CompleteTrackPointer(e.GetPosition(this));
    }
    private void OnTrackPointerCaptureLost(object sender, MouseEventArgs e)
    {
        if (trackPointerCaptured) CancelTrackPointer();
    }
    private void OnTrackPointerLeave(object sender, MouseEventArgs e)
    {
        if (!IsTrackDragActive) CancelTrackPointer();
    }
    private void OnTrackDragKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleTrackDragKey(e.Key)) e.Handled = true;
    }
    private void OnTrackDragUnloaded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, this)) CancelTrackPointer();
    }

    private static DependencyObject? TrackInputParent(DependencyObject element) => element is ContentElement content
        ? ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent : GetInputParent(element);

    internal bool BeginTrackPointer(DependencyObject? origin, Point point)
    {
        CancelTrackPointer();
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || deactivationStarted || viewModel is null ||
            IsVolumeOverlayOpen || nodeSourceTrack is not null) return false;
        OHsWorkstationTrackRow? row = null;
        for (var current = origin; current is not null; current = TrackInputParent(current))
        {
            if (current is OHsWorkstationTrackRow found) { row = found; break; }
            if (current is ListBoxItem container) { row = FindVisualChild<OHsWorkstationTrackRow>(container); break; }
            if (current is ButtonBase or BalloonButton or TextBoxBase or PasswordBox or Selector or RangeBase or Thumb or
                MenuItem or BeatGridView or WaveformView or PianoRollView) return false;
        }
        if (row?.Track is not { } track || !IsWithin(row, TrackList) || !CanReorderTrack(track)) return false;
        if (!GetTrackDragViewport().Contains(TranslatePoint(point, TrackList))) return false;
        if (row.SampleTimeline is { } grid && grid.IsArrangeValid &&
            grid.TransformToAncestor(this).TransformBounds(new Rect(grid.RenderSize)).Contains(point)) return false;
        trackDragOwner = viewModel; trackDragProject = viewModel.Project; trackDragGeneration = viewModel.LoadGeneration;
        DraggedTrack = track; trackPointerStart = point;
        return true;
    }

    private bool CanReorderTrack(Track track) => !deactivationStarted && !IsVolumeOverlayOpen && viewModel is not null &&
        (viewModel.MoveTrackUpCommand.CanExecute(track) || viewModel.MoveTrackDownCommand.CanExecute(track));

    private bool IsTrackSessionCurrent() => DraggedTrack is { } track && ReferenceEquals(trackDragOwner, viewModel) &&
        ReferenceEquals(trackDragProject, viewModel.Project) && trackDragGeneration == viewModel.LoadGeneration && CanReorderTrack(track);

    internal void UpdateTrackPointer(Point point, bool leftButtonPressed)
    {
        if (DraggedTrack is null) return;
        if (!leftButtonPressed || !double.IsFinite(point.X) || !double.IsFinite(point.Y) || !IsTrackSessionCurrent())
        { CancelTrackPointer(); return; }
        trackPointerLast = point;
        try
        {
            if (!IsTrackDragActive)
            {
                if (!OHsTrackDragGeometry.CrossedThreshold(trackPointerStart, point,
                    SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance)) return;
                trackPointerCaptured = CaptureTrackPointer?.Invoke() ?? CaptureMouse();
                if (!trackPointerCaptured) { CancelTrackPointer(); return; }
                IsTrackDragActive = true;
                trackDragLayer = AdornerLayer.GetAdornerLayer(TrackList);
                if (trackDragLayer is null) { CancelTrackPointer(); return; }
                trackDragAdorner = new OHsTrackDragAdorner(TrackList,
                    (DataTemplate)FindResource("OHsWorkstationTrackDragTagTemplate"), (DataTemplate)FindResource("OHsWorkstationTrackDragPhantomTemplate"));
                trackDragLayer.Add(trackDragAdorner);
                TrackList.LayoutUpdated += OnTrackDragLayoutUpdated;
                SystemParameters.StaticPropertyChanged += OnTrackDragSettingsChanged;
            }
            RefreshTrackDragPresentation();
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Track drag failed: {0}", error);
            CancelTrackPointer();
        }
    }

    internal bool HandleTrackDragKey(Key key)
    {
        if (key != Key.Escape || DraggedTrack is null) return false;
        CancelTrackPointer(); return true;
    }

    internal bool HandleTrackDragWheel(ModifierKeys modifiers, int delta)
    {
        if (!IsTrackDragActive || modifiers != ModifierKeys.None) { trackWheelRemainder = 0; return false; }
        if (!IsTrackSessionCurrent()) { CancelTrackPointer(); return false; }
        if (FindVisualChild<ScrollViewer>(TrackList) is not { } scroll) { CancelTrackPointer(); return false; }
        trackWheelRemainder += delta;
        var steps = trackWheelRemainder / Mouse.MouseWheelDeltaForOneLine;
        trackWheelRemainder %= Mouse.MouseWheelDeltaForOneLine;
        var lines = SystemParameters.WheelScrollLines;
        for (var step = 0; step < Math.Abs(steps); step++)
        {
            if (lines < 0) { if (steps > 0) scroll.PageUp(); else scroll.PageDown(); }
            else for (var line = 0; line < lines; line++)
            { if (steps > 0) scroll.LineUp(); else scroll.LineDown(); }
        }
        return true;
    }

    internal bool CompleteTrackPointer(Point point)
    {
        try
        {
            if (!IsTrackDragActive || !IsTrackSessionCurrent()) return false;
            TrackList.UpdateLayout();
            UpdateTrackPointer(point, true);
            TrackList.UpdateLayout();
            RefreshTrackDragPresentation();
            if (!IsTrackDragActive || !IsTrackSessionCurrent() || TrackInsertionSlot is not { } slot) return false;
            var source = DraggedTrack!;
            var owner = viewModel; var project = trackDragProject; var generation = trackDragGeneration; var revision = trackOrderRevision;
            var index = OHsTrackDragGeometry.ToFinalIndex(owner.Tracks.IndexOf(source), slot, owner.Tracks.Count);
            if (index is null || index == owner.Tracks.IndexOf(source)) return false;
            // Releasing native capture can synchronously invoke user code. Validate again afterwards.
            CancelTrackPointer();
            if (!ReferenceEquals(owner, viewModel) || !ReferenceEquals(project, owner.Project) || generation != owner.LoadGeneration ||
                revision != trackOrderRevision || !CanReorderTrack(source)) return false;
            return owner.MoveTrack(source, index.Value);
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Track drop failed: {0}", error);
            return false;
        }
        finally { CancelTrackPointer(); }
    }

    internal void CancelTrackPointer()
    {
        var release = trackPointerCaptured;
        trackPointerCaptured = false; IsTrackDragActive = false; DraggedTrack = null; TrackInsertionSlot = null;
        trackDragOwner = null; trackDragProject = null; trackWheelRemainder = 0;
        TrackList.LayoutUpdated -= OnTrackDragLayoutUpdated;
        SystemParameters.StaticPropertyChanged -= OnTrackDragSettingsChanged;
        foreach (var container in trackDragContainers.ToArray()) DetachTrackDragContainer(container);
        if (trackDragAdorner is not null) trackDragLayer?.Remove(trackDragAdorner);
        trackDragAdorner = null; trackDragLayer = null; trackDragImage = null;
        if (!release) return;
        if (ReleaseTrackPointer is { } substitute) substitute();
        else if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void OnTrackDragLayoutUpdated(object? sender, EventArgs e) => RefreshTrackDragPresentation();
    private void OnTrackDragSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.HighContrast)) return;
        if (Dispatcher.CheckAccess()) RefreshTrackDragPresentation(true);
        else Dispatcher.BeginInvoke(() => RefreshTrackDragPresentation(true));
    }

    private Rect GetTrackDragViewport()
    {
        var scroll = FindVisualChild<ScrollViewer>(TrackList);
        var presenter = scroll is null ? null : FindVisualChild<ScrollContentPresenter>(scroll);
        if (presenter is null || !presenter.IsArrangeValid) return Rect.Empty;
        var bounds = presenter.TransformToAncestor(TrackList).TransformBounds(new Rect(presenter.RenderSize));
        bounds.Intersect(new Rect(TrackList.RenderSize));
        return bounds;
    }

    private void RefreshTrackDragPresentation(bool forceImage = false)
    {
        if (!IsTrackDragActive || refreshingTrackDrag) return;
        if (!IsTrackSessionCurrent()) { CancelTrackPointer(); return; }
        if (!TrackList.IsArrangeValid) return;
        refreshingTrackDrag = true;
        try
        {
            var source = DraggedTrack!;
            var state = (viewModel.GetTrackExtent(source), source.Name, source.IsCollapsed, source.IsTrackMuted,
                source.IsTrackSoloed, source.Volume, source.SelectedSampleFileName, TimelineContentWidth, TimelineHorizontalOffset);
            var rows = RealizedRows(TrackList).Select(row => (Row: row, Container: FindAncestor<ListBoxItem>(row)))
                .Where(pair => pair.Container is not null && pair.Row.IsArrangeValid)
                .Select(pair => (pair.Row, Container: pair.Container!, Index: TrackList.ItemContainerGenerator.IndexFromContainer(pair.Container!),
                    Bounds: pair.Row.TransformToAncestor(TrackList).TransformBounds(new Rect(pair.Row.RenderSize))))
                .Where(pair => pair.Index >= 0).OrderBy(pair => pair.Index).ToArray();
            if (forceImage || trackDragImage is null || state != trackImageState)
            {
                var sourceRow = rows.FirstOrDefault(pair => ReferenceEquals(pair.Row.Track, source)).Row;
                if (sourceRow is null) { CancelTrackPointer(); return; }
                sourceRow.RefreshPresentation();
                var sourceContainer = FindAncestor<ListBoxItem>(sourceRow)!;
                OHsTrackDragPresentation.SetIsSource(sourceContainer, false);
                trackDragImage = CaptureTrackPreview is { } substitute ? substitute(sourceRow) : CaptureTrackRowImage(sourceRow);
                if (trackDragImage is null) { CancelTrackPointer(); return; }
                // The public row extent owns collapse/expanded sizing; image pixels do not.
                state.Item1 = sourceRow.CurrentTrackExtent;
                trackImageState = state;
            }
            var viewport = GetTrackDragViewport();
            if (viewport.IsEmpty || viewport.Width <= 0 || viewport.Height <= 0) { CancelTrackPointer(); return; }
            Rect? oldGap = null;
            foreach (var pair in rows)
            {
                var before = OHsTrackDragPresentation.GetGapBefore(pair.Container);
                var after = OHsTrackDragPresentation.GetGapAfter(pair.Container);
                if (before > 0) oldGap = new Rect(pair.Bounds.Left, pair.Bounds.Top - before, pair.Bounds.Width, before);
                else if (after > 0) oldGap = new Rect(pair.Bounds.Left, pair.Bounds.Bottom, pair.Bounds.Width, after);
            }
            var pointer = TranslatePoint(trackPointerLast, TrackList);
            TrackInsertionSlot = OHsTrackDragGeometry.FindInsertionSlot(pointer, viewport,
                rows.Select(pair => (pair.Index, pair.Bounds)).ToArray(), viewModel.Tracks.Count, TrackInsertionSlot, oldGap);
            var beforeHost = rows.FirstOrDefault(pair => pair.Index == TrackInsertionSlot).Container;
            var afterHost = beforeHost is null ? rows.FirstOrDefault(pair => pair.Index + 1 == TrackInsertionSlot).Container : null;
            var host = rows.FirstOrDefault(pair => ReferenceEquals(pair.Container, beforeHost ?? afterHost));
            var gap = host.Container is null ? (Rect?)null : new Rect(viewport.Left,
                beforeHost is not null ? host.Bounds.Top - OHsTrackDragPresentation.GetGapBefore(host.Container) : host.Bounds.Bottom,
                viewport.Width, trackImageState.Extent);
            var liveContainers = rows.Select(pair => pair.Container).ToHashSet();
            foreach (var old in trackDragContainers.Except(liveContainers).ToArray()) DetachTrackDragContainer(old);
            foreach (var pair in rows)
            {
                if (trackDragContainers.Add(pair.Container))
                {
                    pair.Container.DataContextChanged += OnTrackContainerRecycled;
                    pair.Container.Unloaded += OnTrackContainerUnloaded;
                }
                OHsTrackDragPresentation.SetIsSource(pair.Container, ReferenceEquals(pair.Row.Track, source));
                OHsTrackDragPresentation.SetGapBefore(pair.Container, ReferenceEquals(pair.Container, beforeHost) ? trackImageState.Extent : 0);
                OHsTrackDragPresentation.SetGapAfter(pair.Container, ReferenceEquals(pair.Container, afterHost) ? trackImageState.Extent : 0);
            }
            trackDragAdorner?.Update(pointer, viewport, gap, source.Name, trackDragImage!, trackImageState.Extent);
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Track preview failed: {0}", error);
            CancelTrackPointer();
        }
        finally { refreshingTrackDrag = false; }
    }

    private static ImageSource? CaptureTrackRowImage(OHsWorkstationTrackRow row)
    {
        var size = row.RenderSize;
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0) return null;
        var image = new RenderTargetBitmap(checked((int)Math.Ceiling(size.Width)), checked((int)Math.Ceiling(size.Height)), 96, 96, PixelFormats.Pbgra32);
        image.Render(row); image.Freeze();
        return image;
    }
    private void OnTrackContainerRecycled(object sender, DependencyPropertyChangedEventArgs e) => DetachTrackDragContainer((ListBoxItem)sender);
    private void OnTrackContainerUnloaded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender)) DetachTrackDragContainer((ListBoxItem)sender);
    }
    private void DetachTrackDragContainer(ListBoxItem container)
    {
        container.DataContextChanged -= OnTrackContainerRecycled; container.Unloaded -= OnTrackContainerUnloaded;
        container.ClearValue(OHsTrackDragPresentation.IsSourceProperty);
        container.ClearValue(OHsTrackDragPresentation.GapBeforeProperty);
        container.ClearValue(OHsTrackDragPresentation.GapAfterProperty);
        trackDragContainers.Remove(container);
    }
}
