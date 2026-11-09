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

public partial class WorkstationPage : UserControl
{
    public static readonly DependencyProperty TimelineContentWidthProperty = DependencyProperty.Register(nameof(TimelineContentWidth), typeof(double), typeof(WorkstationPage));
    public static readonly DependencyProperty TimelineViewportWidthProperty = DependencyProperty.Register(nameof(TimelineViewportWidth), typeof(double), typeof(WorkstationPage));
    public static readonly DependencyProperty TimelineHorizontalOffsetProperty = DependencyProperty.Register(nameof(TimelineHorizontalOffset), typeof(double), typeof(WorkstationPage));
    public double TimelineContentWidth { get => (double)GetValue(TimelineContentWidthProperty); set => SetValue(TimelineContentWidthProperty, value); }
    public double TimelineViewportWidth { get => (double)GetValue(TimelineViewportWidthProperty); set => SetValue(TimelineViewportWidthProperty, value); }
    public double TimelineHorizontalOffset { get => (double)GetValue(TimelineHorizontalOffsetProperty); set => SetValue(TimelineHorizontalOffsetProperty, value); }
    private readonly WorkstationPageVM viewModel;
    internal OHsWorkstationToolbar Toolbar => ToolbarComponent;
    public OHsWorkstationTimeline Timeline => AudioCard.Timeline;
    internal OHsWorkstationFooter Footer => Toolbar.Footer;
    internal Task NodeInputCompletion => TrackListComponent.NodeInputCompletion;
    internal bool IsRegionSelectionActive => TrackListComponent.IsRegionSelectionActive;
    internal IInputElement FindRemovalFocusTarget(int index) => TrackListComponent.FindRemovalFocusTarget(index) ?? Timeline.FocusTarget;
    internal bool BeginNodePointer(BeatGridView? grid, Point gridPoint, Point pagePoint, ModifierKeys modifiers = ModifierKeys.None) =>
        TrackListComponent.BeginNodePointer(grid, gridPoint, TranslatePoint(pagePoint, TrackListComponent), modifiers);
    internal void UpdateNodePointer(Point pagePoint, BeatGridView? targetGrid, Point targetPoint, ModifierKeys modifiers = ModifierKeys.None) =>
        TrackListComponent.UpdateNodePointer(TranslatePoint(pagePoint, TrackListComponent), targetGrid, targetPoint, modifiers);
    internal Task<bool> CompleteNodePointerAsync() => TrackListComponent.CompleteNodePointerAsync();
    internal void CancelNodePointer() => TrackListComponent.CancelNodePointer();
    private bool HandleNodeKey(Key key, ModifierKeys modifiers, bool isRepeat, DependencyObject? origin) => TrackListComponent.HandleNodeKey(key, modifiers, isRepeat, origin);
    private bool HandleNodeDragWheel(ModifierKeys modifiers, int delta) => TrackListComponent.HandleNodeDragWheel(modifiers, delta);
    private void OnNodeOwnerDeactivated(object? sender, EventArgs e) { CancelNodePointer(); Timeline.Reset(); }
    private void OnPagePointerDown(object sender, MouseButtonEventArgs e)
    {
        var origin = e.OriginalSource as DependencyObject;
        if (IsDrawerOrigin(origin)) return;
        if (Toolbar.IsCollapseActionOrigin(origin)) return;
        if (!deactivationStarted && !IsWithin(origin, TracksCard)) viewModel.SelectedTrack = null;
        if (!IsWithin(origin, TrackListComponent)) TrackListComponent.BeginNodePointer(null, default, default);
    }
    private void OnTrackViewportChanged(object? sender, EventArgs e)
    {
        if (bottomDrawerHost is null)
            TrackScrollBarGutter = TrackListComponent.TrackScrollBarGutter;
        readinessCandidate = null;
    }
    private bool renderingSubscribed;
    private Window? ownerWindow;
    private TimeSpan lastPresentationFrame = TimeSpan.MinValue;
    private (int Generation, Size Size, double Width, double Viewport)? readinessCandidate;
    private readonly TaskCompletionSource presentationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool playbackReady;
    private string? projectPath;
    private bool loadStarted;
    private bool deactivationStarted;
    public WorkstationPage(WorkstationPageVM viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        VolumeOverlay.SetBinding(DataContextProperty, new Binding(nameof(DataContext)) { Source = this, Mode = BindingMode.OneWay });
        VolumeOverlay.SetBinding(FlowDirectionProperty, new Binding(nameof(FlowDirection)) { Source = this, Mode = BindingMode.OneWay });
        SizeChanged += (_, args) => UpdateResponsiveLayout(args.NewSize);
        Timeline.AttachScrollBar(WaveformScrollBar);
        Timeline.ViewportChanged += OnTimelineViewportChanged;
        Toolbar.MarkerModeRequested += OnMarkerModeRequested;
        DataContext = viewModel;
        RefreshHeaderDimming();
        RefreshLoadingPresentation();
        SystemParameters.StaticPropertyChanged += OnLoadingSystemChanged;
        TrackListComponent.ViewportChanged += OnTrackViewportChanged;
        PreviewMouseLeftButtonDown += OnPagePointerDown;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnPlaybackVisibilityChanged;
        viewModel.PropertyChanging += OnViewModelPropertyChanging;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

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
        markerModeCancellation?.Cancel();
        Toolbar.CloseMenus();
        if (deactivationStarted) return LoadCompletion;
        CancelNodePointer();
        ResetPianoRollExpansion();
        if (sideDrawerHost is { } sideHost) ClearDrawerHostAnimation(sideHost);
        if (bottomDrawerHost is { } bottomHost) ClearDrawerHostAnimation(bottomHost);
        Timeline.Reset();
        deactivationStarted = true;
        SystemParameters.StaticPropertyChanged -= OnLoadingSystemChanged;
        RefreshHeaderDimming();
        RefreshLoadingPresentation();
        TrackListComponent.Deactivate();
        presentationReady.TrySetResult();
        UpdatePlaybackRefreshSubscription();
        LoadCompletion = DeactivateCoreAsync();
        return LoadCompletion;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (loadStarted || projectPath is null) return;
        loadStarted = true;
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
        if (viewModel.LoadPhase == Hitbench.Services.ProjectLoadPhase.AwaitingPresentation)
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
        try { await MarkerModeChangeCompletion; await Timeline.CancelNavigationAsync(); await viewModel.DeactivateAsync(); await PlaybackInputCompletion; await NodeInputCompletion; }
        finally
        {
            if (ownerWindow is not null) { ownerWindow.StateChanged -= OnOwnerStateChanged; ownerWindow.Deactivated -= OnNodeOwnerDeactivated; }
            Loaded -= OnLoaded;
            Unloaded -= OnUnloaded;
            IsVisibleChanged -= OnPlaybackVisibilityChanged;
            viewModel.PropertyChanging -= OnViewModelPropertyChanging;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            TrackListComponent.ViewportChanged -= OnTrackViewportChanged;
            Timeline.Reset();
            Timeline.DetachScrollBar();
            Toolbar.MarkerModeRequested -= OnMarkerModeRequested;
            PreviewMouseLeftButtonDown -= OnPagePointerDown;
            Timeline.ViewportChanged -= OnTimelineViewportChanged;
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
        if (viewModel.LoadPhase == Hitbench.Services.ProjectLoadPhase.AwaitingPresentation)
        {
            if (!IsMeasureValid || !IsArrangeValid || !Timeline.PreparePresentation() || !TrackListComponent.AreTracksArranged)
            { readinessCandidate = null; return; }
            TrackListComponent.PrepareTrackDrawings();
            var candidate = (viewModel.LoadGeneration, RenderSize, TimelineContentWidth, TimelineViewportWidth);
            if (readinessCandidate != candidate) { readinessCandidate = candidate; return; }
            viewModel.AcknowledgePresentationReady(viewModel.LoadGeneration);
            playbackReady = true;
            presentationReady.TrySetResult();
        }
        if (playbackReady && viewModel.IsPlaying)
        {
            viewModel.RefreshPlayback();
            if (viewModel.IsStaticMarker) viewModel.AdvancePlaybackPresentation(frameTime);
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
        viewModel.ResetPlaybackPresentation();
        if (playbackReady && !deactivationStarted && IsVisible) viewModel.RefreshPlayback();
        UpdatePlaybackRefreshSubscription();
    }

    public Task FlushSavesAsync() => viewModel.FlushSavesAsync();
    public Task SettleInstrumentalEditorAsync() => viewModel.SettleInstrumentalEditorAsync();
    public Task SettleWorkstationDrawersAsync() => viewModel.SettleWorkstationDrawersAsync();
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

    private void OnViewModelPropertyChanging(object? sender, PropertyChangingEventArgs e)
    {
        if (e.PropertyName == nameof(WorkstationPageVM.AreTracksCollapsed) && !viewModel.AreTracksCollapsed)
            RestoreTrackFocusBeforeCollapse(Keyboard.FocusedElement as DependencyObject ??
                FocusManager.GetFocusedElement(FocusManager.GetFocusScope(AudioCard)) as DependencyObject);
        if (e.PropertyName == nameof(WorkstationPageVM.ZoomFactor)) Timeline.CaptureZoomAnchor();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkstationPageVM.StatusText) or nameof(WorkstationPageVM.ShowStatusOverlay)) RefreshLoadingPresentation();
        if (e.PropertyName == nameof(WorkstationPageVM.ShowStatusOverlay)) RefreshHeaderDimming();
        if (e.PropertyName == nameof(WorkstationPageVM.LoadPhase) && viewModel.LoadPhase == Hitbench.Services.ProjectLoadPhase.PreparingData)
        {
            playbackReady = false;
            readinessCandidate = null;
            Timeline.Reset();
            CancelNodePointer();
        }
        if (e.PropertyName is nameof(WorkstationPageVM.IsPlaying) or nameof(WorkstationPageVM.LoadPhase))
            UpdatePlaybackRefreshSubscription();
        if (e.PropertyName == nameof(WorkstationPageVM.LoadPhase) && viewModel.IsLoading) markerModeCancellation?.Cancel();
        if (e.PropertyName == nameof(WorkstationPageVM.ZoomFactor)) { Timeline.ApplyPendingZoomAnchor(); TrackListComponent.RefreshViewport(); }
        if (e.PropertyName == nameof(WorkstationPageVM.Position) && !viewModel.IsStaticMarker && viewModel.IsPlaying) FollowPlaybackPosition();
        if (e.PropertyName == nameof(WorkstationPageVM.DisplayPosition) && viewModel.IsStaticMarker && !viewModel.IsPlaying) FollowPlaybackPosition();
        if (e.PropertyName == nameof(WorkstationPageVM.ScrollWithMarker) && viewModel.ScrollWithMarker && viewModel.IsPlaying)
        {
            viewModel.RefreshPlayback();
            FollowPlaybackPosition();
        }
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
        if (IsMarkerModeTransitionActive) return true;
        if (key == Key.F8 && modifiers == ModifierKeys.None && !IsTextInputOrigin(origin))
        {
            if (!viewModel.CanPlay) return false;
            if (!isRepeat) MarkerModeChangeCompletion = ChangeMarkerModeAsync(!viewModel.IsStaticMarker);
            return true;
        }
        if (FindAncestor<Hitbench.Views.Popups.OHsControlPointEditor>(origin) is not null) return false;
        if (FindAncestor<OHsControlPointZone>(origin) is { } zone)
        {
            if (FindAncestor<OHsControlPointLane>(zone)?.HandlePointKey(key, modifiers, origin) == true) return true;
            if (key is Key.Left or Key.Right) return true;
            if (key is Key.Space or Key.Enter) return false;
        }
        if (IsVolumeOverlayOpen) return true;
        if (deactivationStarted) return false;
        if (HandleWorkstationShortcut(key, modifiers, isRepeat, origin)) return true;
        if (HandleNodeKey(key, modifiers, isRepeat, origin)) return true;
        if (!viewModel.CanPlay) return false;
        if (HandleBeatDivisorKey(key, modifiers, isRepeat, origin)) return true;
        if (modifiers == ModifierKeys.Control && key is Key.Z or Key.Y)
        {
            if (IsTextInputOrigin(origin)) return false;
            if (!viewModel.CanPlaceSamples) return false;
            if (isRepeat) return true;
            var historyCommand = key == Key.Z ? viewModel.UndoCommand : viewModel.RedoCommand;
            if (!historyCommand.CanExecute(null)) return false;
            historyCommand.Execute(null);
            return true;
        }
        if (key == Key.Space)
        {
            if (IsTextInputOrigin(origin)) return false;
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
            else if (viewModel.IsStaticMarker) Timeline.ScrollFromUser(key == Key.Home ? Timeline.MinimumHorizontalOffset : Timeline.ScrollableWidth);
            else if (key == Key.Home) Timeline.ScrollToHorizontalOffset(0);
            else Timeline.ScrollToHorizontalOffset(Timeline.ScrollableWidth);
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

    private static bool IsTextInputOrigin(DependencyObject? origin) =>
        FindAncestor<TextBoxBase>(origin) is not null || FindAncestor<PasswordBox>(origin) is not null ||
        FindAncestor<ComboBox>(origin)?.IsEditable == true;

    private bool HandleWorkstationShortcut(Key key, ModifierKeys modifiers, bool isRepeat, DependencyObject? origin)
    {
        if (modifiers == ModifierKeys.None && key is Key.D1 or Key.NumPad1 or Key.D2 or Key.NumPad2)
        {
            if (!viewModel.CanPlaceSamples || IsTextInputOrigin(origin)) return false;
            if (!isRepeat) viewModel.IsAdditionMode = key is Key.D2 or Key.NumPad2;
            return true;
        }
        if (modifiers == ModifierKeys.None && key == Key.F)
        {
            if (!viewModel.CanToggleScrollWithMarker || IsTextInputOrigin(origin)) return false;
            if (!isRepeat) viewModel.ScrollWithMarker = !viewModel.ScrollWithMarker;
            return true;
        }
        ICommand? command = (key, modifiers) switch
        {
            (Key.N, ModifierKeys.Control) => viewModel.AddSampleTrackCommand,
            (Key.N, ModifierKeys.Control | ModifierKeys.Shift) => viewModel.AddPianoTrackCommand,
            (Key.F2, ModifierKeys.None) => viewModel.ManageSamplesCommand,
            (Key.F4, ModifierKeys.None) => viewModel.ToggleTracksCollapsedCommand,
            (Key.F5, ModifierKeys.None) => viewModel.ReloadSamplesCommand,
            (Key.F6, ModifierKeys.None) => viewModel.AddControlPointCommand,
            _ => null
        };
        if (command is null || !command.CanExecute(null)) return false;
        if (!isRepeat)
        {
            if (command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand asyncCommand)
                PlaybackInputCompletion = ObserveWorkstationInputAsync(asyncCommand.ExecuteAsync(null));
            else command.Execute(null);
        }
        return true;
    }

    private static async Task ObserveWorkstationInputAsync(Task operation)
    {
        try { await operation; }
        catch (OperationCanceledException) { }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Workstation input failed: {0}", error); }
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

    private static DependencyObject? GetInputParent(DependencyObject current) =>
        current is Visual or Visual3D
            ? VisualTreeHelper.GetParent(current)
            : current is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(current);

    internal bool HandleWaveformWheel(ModifierKeys modifiers, int delta)
    {
        if (IsMarkerModeTransitionActive) { Timeline.ResetWheel(); return true; }
        if (IsVolumeOverlayOpen && modifiers != (ModifierKeys.Control | ModifierKeys.Alt)) { Timeline.ResetWheel(); return true; }
        return Timeline.HandleWaveformWheel(modifiers, delta);
    }
    private void RevealPlaybackPosition() => Timeline.RevealPlaybackPosition();
    private void FollowPlaybackPosition()
    {
        if (playbackReady && !deactivationStarted && IsVisible && ownerWindow?.WindowState != WindowState.Minimized)
            Timeline.FollowPlaybackPosition();
    }
    internal static double GetPlaybackPageOffset(double markerX, double offset, double viewportWidth, double extentWidth) =>
        OHsWorkstationTimeline.GetPlaybackPageOffset(markerX, offset, viewportWidth, extentWidth);
    private void OnTimelineViewportChanged(object? sender, EventArgs e)
    {
        TimelineContentWidth = Timeline.TimelineContentWidth;
        TimelineViewportWidth = Timeline.TimelineViewportWidth;
        TimelineHorizontalOffset = Timeline.TimelineHorizontalOffset;
        TrackListComponent.RefreshViewport();

    }
}
