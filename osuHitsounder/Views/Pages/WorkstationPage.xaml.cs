using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.ComponentModel;
using System.Collections.Specialized;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Resources.CustomControls;
using Track = osuHitsounder.Core.Entities.Track;

namespace osuHitsounder.Views.Pages;

public partial class WorkstationPage : UserControl
{
    public static readonly DependencyProperty TimelineContentWidthProperty = DependencyProperty.Register(nameof(TimelineContentWidth), typeof(double), typeof(WorkstationPage));
    public static readonly DependencyProperty TimelineViewportWidthProperty = DependencyProperty.Register(nameof(TimelineViewportWidth), typeof(double), typeof(WorkstationPage));
    public static readonly DependencyProperty TimelineHorizontalOffsetProperty = DependencyProperty.Register(nameof(TimelineHorizontalOffset), typeof(double), typeof(WorkstationPage));
    public double TimelineContentWidth { get => (double)GetValue(TimelineContentWidthProperty); set => SetValue(TimelineContentWidthProperty, value); }
    public double TimelineViewportWidth { get => (double)GetValue(TimelineViewportWidthProperty); set => SetValue(TimelineViewportWidthProperty, value); }
    public double TimelineHorizontalOffset { get => (double)GetValue(TimelineHorizontalOffsetProperty); set => SetValue(TimelineHorizontalOffsetProperty, value); }
    private readonly WorkstationPageVM viewModel;
    private bool renderingSubscribed;
    private Window? ownerWindow;
    private TimeSpan lastPresentationFrame = TimeSpan.MinValue;
    private (int Generation, Size Size, double Width, double Viewport)? readinessCandidate;
    private readonly TaskCompletionSource presentationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool playbackReady;
    private string? projectPath;
    private bool loadStarted;
    private bool deactivationStarted;
    private bool hasPendingZoomAnchor;
    private double zoomAnchorFraction;
    private double zoomAnchorViewportX;
    private ModifierKeys wheelMode;
    private int wheelRemainder;

    public WorkstationPage(WorkstationPageVM viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        WaveformScrollBar.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnWaveformScrollBarDragStarted));
        DataContext = viewModel;
        InitializeNodeInteraction();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnPlaybackVisibilityChanged;
        viewModel.PropertyChanging += OnViewModelPropertyChanging;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.Tracks.CollectionChanged += OnTracksCollectionChanged;
    }

    public Task LoadCompletion { get; private set; } = Task.CompletedTask;
    internal Task PlaybackInputCompletion { get; private set; } = Task.CompletedTask;

    public void SetProjectPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (loadStarted) throw new InvalidOperationException("The workstation has already started loading.");
        projectPath = value;
    }

    public Task DeactivateAsync()
    {
        if (deactivationStarted) return LoadCompletion;
        CancelNodePointer();
        deactivationStarted = true;
        presentationReady.TrySetResult();
        UpdatePlaybackRefreshSubscription();
        LoadCompletion = DeactivateCoreAsync();
        return LoadCompletion;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (loadStarted || projectPath is null) return;
        loadStarted = true;
        (System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(LoadingProjectText) ??
            System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(LoadingProjectText))?
            .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        ownerWindow = Window.GetWindow(this);
        if (ownerWindow is not null) { ownerWindow.StateChanged += OnOwnerStateChanged; ownerWindow.Deactivated += OnNodeOwnerDeactivated; }
        UpdatePlaybackRefreshSubscription();
        LoadCompletion = LoadCoreAsync(projectPath);
        try { await LoadCompletion; }
        catch (OperationCanceledException) { }
    }

    private async Task LoadCoreAsync(string path)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (deactivationStarted) return;
        await viewModel.LoadAsync(path, CancellationToken.None);
        UpdatePlaybackRefreshSubscription();
        if (viewModel.LoadPhase == osuHitsounder.Services.ProjectLoadPhase.AwaitingPresentation)
            await presentationReady.Task;
        if (!deactivationStarted && viewModel.Waveform is not null)
        {
            playbackReady = true;
            UpdatePlaybackRefreshSubscription();
        }
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        try { await DeactivateAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Workstation deactivation failed: {0}", error); }
    }

    private async Task DeactivateCoreAsync()
    {
        DismissVolumeOverlay();
        try { await viewModel.DeactivateAsync(); await PlaybackInputCompletion; await NodeInputCompletion; }
        finally
        {
            if (ownerWindow is not null) { ownerWindow.StateChanged -= OnOwnerStateChanged; ownerWindow.Deactivated -= OnNodeOwnerDeactivated; }
            Loaded -= OnLoaded;
            Unloaded -= OnUnloaded;
            IsVisibleChanged -= OnPlaybackVisibilityChanged;
            viewModel.PropertyChanging -= OnViewModelPropertyChanging;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.Tracks.CollectionChanged -= OnTracksCollectionChanged;
            hasPendingZoomAnchor = false;
            wheelMode = ModifierKeys.None;
            wheelRemainder = 0;
        }
    }

    internal bool IsPlaybackRefreshSubscribed => renderingSubscribed;

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs frame) ProcessPresentationFrame(frame.RenderingTime);
    }

    internal void ProcessPresentationFrame(TimeSpan frameTime)
    {
        if (deactivationStarted || !IsVisible || ownerWindow?.WindowState == WindowState.Minimized || frameTime == lastPresentationFrame) return;
        lastPresentationFrame = frameTime;
        if (viewModel.LoadPhase == osuHitsounder.Services.ProjectLoadPhase.AwaitingPresentation)
        {
            var boundary = WaveformScrollViewer.BorderThickness;
            var padding = WaveformScrollViewer.Padding;
            var hasWaveformSpace = WaveformScrollViewer.ActualWidth > boundary.Left + boundary.Right + padding.Left + padding.Right;
            if (!IsMeasureValid || !IsArrangeValid || (hasWaveformSpace && WaveformScrollViewer.ViewportWidth <= 0) ||
                !WaveformDisplay.IsMeasureValid || !WaveformDisplay.IsArrangeValid || !TrackList.IsArrangeValid)
            { readinessCandidate = null; return; }
            WaveformDisplay.PrepareDrawing();
            PrepareTrackDrawings(TrackList);
            var candidate = (viewModel.LoadGeneration, RenderSize, WaveformDisplay.ActualWidth, WaveformScrollViewer.ViewportWidth);
            if (readinessCandidate != candidate) { readinessCandidate = candidate; return; }
            viewModel.AcknowledgePresentationReady(viewModel.LoadGeneration);
            playbackReady = true;
            presentationReady.TrySetResult();
        }
        if (playbackReady && viewModel.IsPlaying)
        {
            viewModel.RefreshPlayback();
            FollowPlaybackPosition();
        }
        UpdatePlaybackRefreshSubscription();
    }

    internal void UpdatePlaybackRefreshSubscription()
    {
        var subscribe = loadStarted && !deactivationStarted && IsVisible && ownerWindow?.WindowState != WindowState.Minimized &&
            (viewModel.IsLoading || viewModel.IsPlaying);
        if (subscribe == renderingSubscribed) return;
        renderingSubscribed = subscribe;
        if (subscribe) CompositionTarget.Rendering += OnRendering;
        else CompositionTarget.Rendering -= OnRendering;
    }

    private void OnOwnerStateChanged(object? sender, EventArgs e) => RefreshVisibility();
    private void OnPlaybackVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => RefreshVisibility();
    private void RefreshVisibility()
    {
        readinessCandidate = null;
        if (playbackReady && !deactivationStarted && IsVisible) viewModel.RefreshPlayback();
        UpdatePlaybackRefreshSubscription();
    }

    private void OnWaveformViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Bootstrap measurement before the ScrollViewer has a nonzero viewport.
        var inset = WaveformScrollViewer.BorderThickness;
        var padding = WaveformScrollViewer.Padding;
        WaveformDisplay.SetCurrentValue(WaveformView.ViewportWidthProperty,
            Math.Max(0, e.NewSize.Width - inset.Left - inset.Right - padding.Left - padding.Right));
        TimelineViewportWidth = WaveformScrollViewer.ViewportWidth;
    }
    private void OnWaveformDisplaySizeChanged(object sender, SizeChangedEventArgs e)
    {
        TimelineContentWidth = e.NewSize.Width;
        ApplyPendingZoomAnchor();
        UpdateNodeAdornerGeometry();
    }
    private void OnWaveformScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidth > 0) WaveformDisplay.SetCurrentValue(WaveformView.ViewportWidthProperty, e.ViewportWidth);
        TimelineHorizontalOffset = e.HorizontalOffset;
        TimelineViewportWidth = e.ViewportWidth;
        WaveformScrollBar.Value = e.HorizontalOffset;
        ApplyPendingZoomAnchor();
        UpdateNodeAdornerGeometry();
    }

    private void OnSampleSelectorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox { Name: "TrackSampleFileSelector", DataContext: Track track } selector)
        {
            selector.Tag = true;
            var saved = track.SelectedSampleFileName;
            selector.SelectedItem = viewModel.SampleFiles.FirstOrDefault(name =>
                string.Equals(name, saved, StringComparison.OrdinalIgnoreCase));
            selector.Tag = false;
            UpdateMissingSampleCue(selector, track);
        }
    }

    private void OnTrackControlDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is ToggleButton toggle) { OnTrackAuditionLoaded(toggle, new RoutedEventArgs()); return; }
        if (sender is ComboBox { Name: "TrackSampleFileSelector" } selector) OnSampleSelectorLoaded(selector, new RoutedEventArgs());
        else if (sender is ComboBox metadata) OnTrackMetadataSelectorLoaded(metadata, new RoutedEventArgs());
        else if (sender is Slider slider) OnVolumeSliderLoaded(slider, new RoutedEventArgs());
        else if (sender is TextBox input) OnTrackNameLoaded(input, new RoutedEventArgs());
    }

    private void OnTrackNameLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: Track track } input) return;
        input.Tag = true;
        input.SetCurrentValue(TextBox.TextProperty, track.Name);
        input.Tag = false;
    }

    private void OnTrackNameTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { IsLoaded: true, DataContext: Track track } input && input.Tag is not true)
            viewModel.SetTrackName(track, input.Text);
    }

    private void RefreshSampleSelectors(DependencyObject root)
    {
        if (root is ComboBox { Name: "TrackSampleFileSelector", DataContext: Track } selector)
        {
            OnSampleSelectorLoaded(selector, new RoutedEventArgs());
            return;
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            RefreshSampleSelectors(VisualTreeHelper.GetChild(root, index));
    }

    private void OnSampleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox selector && selector.IsLoaded && selector.Tag is not true && selector.DataContext is Track track)
        {
            if (selector.SelectedItem is string fileName &&
                !string.Equals(track.SelectedSampleFileName, fileName, StringComparison.OrdinalIgnoreCase))
                viewModel.SelectSample(track, fileName);
            UpdateMissingSampleCue(selector, track);
        }
    }

    private void OnTrackMetadataSelectorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: Track track } selector) return;
        selector.Tag = true;
        selector.SetCurrentValue(ComboBox.SelectedItemProperty, selector.Name switch { "TrackSamplesetSelector" => (object)track.Sampleset, "TrackSampleTypeSelector" => track.SampleType, _ => track.Addition });
        selector.Tag = false;
    }

    private void OnTrackMetadataSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { IsLoaded: true, DataContext: Track track } selector || selector.Tag is true) return;
        if (selector.Name == "TrackSampleTypeSelector" && selector.SelectedItem is osuHitsounder.Core.Enums.SampleType type) { viewModel.SetTrackSampleType(track, type); return; }
        if (selector.SelectedItem is not string value) return;
        if (selector.Name == "TrackSamplesetSelector") viewModel.SetTrackSampleset(track, value);
        else if (selector.Name == "TrackAdditionSelector") viewModel.SetTrackAddition(track, value);
    }

    private void OnVolumeSliderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider && slider.DataContext is Track track)
        {
            slider.Tag = true;
            slider.Value = track.Volume;
            slider.Tag = false;
        }
    }

    private void OnVolumeValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && slider.IsLoaded && slider.Tag is not true && slider.DataContext is Track track)
            viewModel.SetTrackVolume(track, (int)Math.Round(e.NewValue));
    }

    private void OnSamplePlacementRequested(object sender, SamplePlacementRequestedEventArgs e)
    {
        if (sender is BeatGridView { DataContext: Track track })
            viewModel.PlaceSample(track, e.LeftBoundaryMilliseconds);
        e.Handled = true;
    }

    private void OnSampleRemovalRequested(object sender, SampleRemovalRequestedEventArgs e)
    {
        if (sender is BeatGridView { DataContext: Track track })
            viewModel.RemoveSample(track, e.Sample);
        e.Handled = true;
    }

    private void UpdateMissingSampleCue(ComboBox selector, Track track)
    {
        RefreshTrackSamplePresentation();
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

    public Task FlushSavesAsync() => viewModel.FlushSavesAsync();
    public bool HasPendingSaves => viewModel.HasPendingSaves;

    public async Task<bool> PrepareToReturnAsync(CancellationToken token)
    {
        var focus = Keyboard.FocusedElement as UIElement;
        DismissVolumeOverlay();
        var prepared = await viewModel.PrepareToReturnAsync(token);
        if (!prepared && !deactivationStarted && focus is { IsVisible: true, IsEnabled: true }) focus.Focus();
        return prepared;
    }

    public void CancelReturn() => viewModel.CancelReturn();

    private void OnEditingModeChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio) viewModel.IsAdditionMode = radio.Name == "AdditionModeButton";
    }

    private void OnWaveformScrollBarDragStarted(object sender, DragStartedEventArgs e)
    {
        if (viewModel.CanPlay && !deactivationStarted) viewModel.ScrollWithMarker = false;
    }

    private void OnWaveformScrollBarScroll(object sender, ScrollEventArgs e)
    {
        if (!viewModel.CanPlay || deactivationStarted) return;
        viewModel.ScrollWithMarker = false;
        WaveformScrollViewer.ScrollToHorizontalOffset(e.NewValue);
    }

    private void OnViewModelPropertyChanging(object? sender, PropertyChangingEventArgs e)
    {
        if (e.PropertyName == nameof(WorkstationPageVM.AreTracksCollapsed) && !viewModel.AreTracksCollapsed)
            RestoreTrackFocusBeforeCollapse(Keyboard.FocusedElement as DependencyObject ??
                FocusManager.GetFocusedElement(FocusManager.GetFocusScope(ToggleTracksCollapsedButton)) as DependencyObject);
        if (e.PropertyName != nameof(WorkstationPageVM.ZoomFactor) || hasPendingZoomAnchor) return;
        var duration = viewModel.Waveform?.Duration.TotalSeconds ?? 0;
        var extent = WaveformDisplay.ActualWidth;
        var viewport = WaveformScrollViewer.ViewportWidth;
        if (!double.IsFinite(duration) || duration <= 0 || !double.IsFinite(extent) || extent <= 0 ||
            !double.IsFinite(viewport) || viewport <= 0) return;

        zoomAnchorFraction = Math.Clamp(viewModel.Position.TotalSeconds / duration, 0, 1);
        var markerViewportX = zoomAnchorFraction * extent - WaveformScrollViewer.HorizontalOffset;
        zoomAnchorViewportX = markerViewportX >= 0 && markerViewportX <= viewport
            ? markerViewportX
            : viewport / 2;
        hasPendingZoomAnchor = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkstationPageVM.TrackAuditionRevision)) RefreshTrackAuditionControls(TrackList);
        if (!viewModel.CanPlaceSamples || nodeDragging && !viewModel.IsNodeDragActive) CancelNodePointer();
        if (e.PropertyName == nameof(WorkstationPageVM.SampleFiles)) RefreshSampleSelectors(TrackList);
        if (e.PropertyName is nameof(WorkstationPageVM.SampleFiles) or nameof(WorkstationPageVM.SamplesRevision)) RefreshTrackSamplePresentation();
        if (e.PropertyName is nameof(WorkstationPageVM.IsPlaying) or nameof(WorkstationPageVM.LoadPhase))
            UpdatePlaybackRefreshSubscription();
        if (e.PropertyName == nameof(WorkstationPageVM.ZoomFactor)) { ApplyPendingZoomAnchor(); UpdateNodeAdornerGeometry(); }
        if (e.PropertyName == nameof(WorkstationPageVM.Position) && viewModel.IsPlaying) FollowPlaybackPosition();
        if (e.PropertyName == nameof(WorkstationPageVM.ScrollWithMarker) && viewModel.ScrollWithMarker && viewModel.IsPlaying)
        {
            viewModel.RefreshPlayback();
            FollowPlaybackPosition();
        }
        if (e.PropertyName == nameof(WorkstationPageVM.StatusText))
            (System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(LoadingProjectText) ??
             System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(LoadingProjectText))?
                .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    private void OnTracksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
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
                        WaveformScrollViewer.Focus();
                });
                break;
            }
        }
    }

    internal IInputElement FindRemovalFocusTarget(int index)
    {
        TrackList.UpdateLayout();
        var container = TrackList.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
        return container is null ? WaveformScrollViewer
            : FindVisualChild<BalloonButton>(container) ?? (IInputElement)WaveformScrollViewer;
    }

    private void ApplyPendingZoomAnchor()
    {
        if (!hasPendingZoomAnchor || deactivationStarted) return;
        var extent = WaveformDisplay.ActualWidth;
        var viewport = WaveformScrollViewer.ViewportWidth;
        if (!double.IsFinite(extent) || extent <= 0 || !double.IsFinite(viewport) || viewport <= 0) return;
        var expectedExtent = WaveformDisplay.ViewportWidth * viewModel.ZoomFactor;
        if (Math.Abs(extent - expectedExtent) > 1 || Math.Abs(WaveformScrollViewer.ExtentWidth - extent) > 1) return;

        var maximum = Math.Max(0, extent - viewport);
        var offset = Math.Clamp(zoomAnchorFraction * extent - zoomAnchorViewportX, 0, maximum);
        hasPendingZoomAnchor = false;
        WaveformScrollViewer.ScrollToHorizontalOffset(offset);
    }

    private void OnWaveformMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        WaveformScrollViewer.Focus();
        if (!WaveformDisplay.SeekAt(e.GetPosition(WaveformDisplay).X)) return;
        e.Handled = true;
    }

    private bool HandleBeatDivisorKey(Key key, ModifierKeys modifiers, bool isRepeat, DependencyObject? origin)
    {
        if (modifiers != ModifierKeys.Control || FindAncestor<TextBoxBase>(origin) is not null ||
            FindAncestor<PasswordBox>(origin) is not null || FindAncestor<ComboBox>(origin)?.IsEditable == true) return false;
        var index = key switch
        {
            Key.D1 or Key.NumPad1 => 0, Key.D2 or Key.NumPad2 => 1, Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3, Key.D5 or Key.NumPad5 => 4, Key.D6 or Key.NumPad6 => 5,
            Key.D8 or Key.NumPad8 => 6, _ => -1
        };
        if (index < 0) return false;
        if (!isRepeat) viewModel.SelectedSubdivisionIndex = index;
        return true;
    }

    internal bool HandlePlaybackKey(Key key, ModifierKeys modifiers, bool isRepeat, DependencyObject? origin)
    {
        if (IsVolumeOverlayOpen) return true;
        if (HandleNodeKey(key, modifiers, isRepeat, origin)) return true;
        if (deactivationStarted || !viewModel.CanPlay) return false;
        if (HandleBeatDivisorKey(key, modifiers, isRepeat, origin)) return true;
        if (modifiers == ModifierKeys.Control && key is Key.Z or Key.Y)
        {
            if (!viewModel.CanPlaceSamples) return false;
            if (isRepeat) return true;
            var historyCommand = key == Key.Z ? viewModel.UndoCommand : viewModel.RedoCommand;
            if (!historyCommand.CanExecute(null)) return false;
            historyCommand.Execute(null);
            return true;
        }
        if (key == Key.Space)
        {
            if (modifiers is not (ModifierKeys.None or ModifierKeys.Shift)) return false;
            var transport = modifiers == ModifierKeys.Shift ? viewModel.StopCommand : viewModel.PlayPauseCommand;
            if (!transport.CanExecute(null)) return false;
            if (!isRepeat) PlaybackInputCompletion = transport.ExecuteAsync(null);
            return true;
        }
        if (modifiers is not (ModifierKeys.None or ModifierKeys.Control)) return false;
        if (key is Key.Home or Key.End)
        {
            if (modifiers == ModifierKeys.Control)
            {
                var target = key == Key.Home ? TimeSpan.Zero : viewModel.Waveform!.Duration;
                if (!viewModel.SeekCommand.CanExecute(target)) return false;
                PlaybackInputCompletion = RevealAfterNavigationAsync(viewModel.SeekCommand.ExecuteAsync(target));
            }
            else if (key == Key.Home) WaveformScrollViewer.ScrollToLeftEnd();
            else WaveformScrollViewer.ScrollToRightEnd();
            return true;
        }
        if (key is not (Key.Left or Key.Right)) return false;
        var command = modifiers == ModifierKeys.Control
            ? key == Key.Left ? viewModel.PreviousWholeBeatCommand : viewModel.NextWholeBeatCommand
            : key == Key.Left ? viewModel.PreviousGridLineCommand : viewModel.NextGridLineCommand;
        if (!command.CanExecute(null)) return false;
        PlaybackInputCompletion = RevealAfterNavigationAsync(command.ExecuteAsync(null), FindAncestor<BeatGridView>(origin));
        return true;
    }

    private static void PrepareTrackDrawings(DependencyObject parent)
    {
        if (parent is BeatGridView grid) grid.PrepareDrawing();
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            PrepareTrackDrawings(VisualTreeHelper.GetChild(parent, index));
    }

    private async Task RevealAfterNavigationAsync(Task navigation, BeatGridView? grid = null)
    {
        try
        {
            await navigation;
            if (!deactivationStarted)
            {
                RevealPlaybackPosition();
                grid?.SetActivePosition(viewModel.Position);
            }
        }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Playback navigation failed: {0}", error); }
    }

    private void RevealPlaybackPosition()
    {
        var duration = viewModel.Waveform?.Duration.TotalSeconds ?? 0;
        var extent = WaveformDisplay.ActualWidth;
        var viewport = WaveformScrollViewer.ViewportWidth;
        if (duration <= 0 || !double.IsFinite(extent) || extent <= 0 ||
            !double.IsFinite(viewport) || viewport <= 0) return;
        var x = Math.Clamp(viewModel.Position.TotalSeconds / duration * extent, 0, extent);
        var offset = WaveformScrollViewer.HorizontalOffset;
        if (x < offset) WaveformScrollViewer.ScrollToHorizontalOffset(x);
        else if (x > offset + viewport)
            WaveformScrollViewer.ScrollToHorizontalOffset(Math.Min(WaveformScrollViewer.ScrollableWidth, x - viewport));
    }

    private void FollowPlaybackPosition()
    {
        if (!viewModel.ScrollWithMarker || !playbackReady || deactivationStarted || !IsVisible || ownerWindow?.WindowState == WindowState.Minimized) return;
        var duration = viewModel.Waveform?.Duration.TotalSeconds ?? 0;
        if (duration <= 0) return;
        var offset = GetPlaybackPageOffset(viewModel.Position.TotalSeconds / duration * WaveformDisplay.ActualWidth,
            WaveformScrollViewer.HorizontalOffset, WaveformScrollViewer.ViewportWidth, WaveformDisplay.ActualWidth);
        if (offset != WaveformScrollViewer.HorizontalOffset) WaveformScrollViewer.ScrollToHorizontalOffset(offset);
    }

    internal static double GetPlaybackPageOffset(double markerX, double offset, double viewportWidth, double extentWidth)
    {
        if (!double.IsFinite(markerX) || !double.IsFinite(offset) || !double.IsFinite(viewportWidth) ||
            !double.IsFinite(extentWidth) || viewportWidth <= 0 || extentWidth <= 0) return offset;
        if (markerX >= offset && markerX < offset + viewportWidth) return offset;
        var pages = Math.Floor((markerX - offset) / viewportWidth);
        return Math.Clamp(offset + pages * viewportWidth, 0, Math.Max(0, extentWidth - viewportWidth));
    }

    private static DependencyObject? GetInputParent(DependencyObject current) =>
        current is Visual or Visual3D
            ? VisualTreeHelper.GetParent(current)
            : current is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(current);

    internal bool HandleWaveformWheel(ModifierKeys modifiers, int delta)
    {
        if (IsVolumeOverlayOpen)
        {
            wheelMode = ModifierKeys.None;
            wheelRemainder = 0;
            return true;
        }
        if (modifiers is not ModifierKeys.Control and not ModifierKeys.Alt and not ModifierKeys.Shift and
            not (ModifierKeys.Control | ModifierKeys.Shift) ||
            !viewModel.CanZoom || deactivationStarted)
        {
            wheelMode = ModifierKeys.None;
            wheelRemainder = 0;
            return false;
        }

        if (wheelMode != modifiers)
        {
            wheelMode = modifiers;
            wheelRemainder = 0;
        }

        wheelRemainder += delta;
        var steps = wheelRemainder / Mouse.MouseWheelDeltaForOneLine;
        wheelRemainder %= Mouse.MouseWheelDeltaForOneLine;
        if (modifiers == ModifierKeys.Shift)
        {
            if (delta != 0) viewModel.ScrollWithMarker = false;
            for (var index = 0; index < Math.Abs(steps); index++)
            {
                if (steps > 0) WaveformScrollViewer.LineLeft();
                else WaveformScrollViewer.LineRight();
            }
            return true;
        }

        var increase = steps > 0;
        for (var index = 0; index < Math.Abs(steps); index++)
        {
            var command = modifiers switch
            {
                ModifierKeys.Control => increase ? viewModel.ZoomInCommand : viewModel.ZoomOutCommand,
                ModifierKeys.Alt => increase ? viewModel.IncreaseSubdivisionCommand : viewModel.DecreaseSubdivisionCommand,
                _ => increase ? viewModel.IncreaseTrackHeightCommand : viewModel.DecreaseTrackHeightCommand
            };
            if (!command.CanExecute(null)) break;
            command.Execute(null);
        }
        return true;
    }

}
