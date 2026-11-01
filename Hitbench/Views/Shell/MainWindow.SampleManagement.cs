using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Data;
using Hitbench.Resources.CustomControls;
using Hitbench.Views.Pages;
using Hitbench.Views.Popups;
using Hitbench.Resources.DataModels;
using Hitbench.Views.Shell;

namespace Hitbench;

public partial class MainWindow
{
    private readonly OHsShellDrawerPresentation sampleDrawerPresentation;
    internal OHsShellDrawerPresentation InstrumentalDrawerPresentation { get; }
    private static readonly DependencyPropertyKey IsSampleManagerVisiblePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsSampleManagerVisible), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsSampleManagerVisibleProperty = IsSampleManagerVisiblePropertyKey.DependencyProperty;
    public bool IsSampleManagerVisible => (bool)GetValue(IsSampleManagerVisibleProperty);
    private static readonly DependencyPropertyKey IsBottomDrawerPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsBottomDrawer), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsBottomDrawerProperty = IsBottomDrawerPropertyKey.DependencyProperty;
    public bool IsBottomDrawer => (bool)GetValue(IsBottomDrawerProperty);
    private static readonly DependencyPropertyKey IsBlockingDrawerVisiblePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsBlockingDrawerVisible), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsBlockingDrawerVisibleProperty = IsBlockingDrawerVisiblePropertyKey.DependencyProperty;
    public bool IsBlockingDrawerVisible => (bool)GetValue(IsBlockingDrawerVisibleProperty);
    private static readonly DependencyPropertyKey IsAnyDrawerVisiblePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsAnyDrawerVisible), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsAnyDrawerVisibleProperty = IsAnyDrawerVisiblePropertyKey.DependencyProperty;
    public bool IsAnyDrawerVisible => (bool)GetValue(IsAnyDrawerVisibleProperty);
    internal OHsSampleDrawerPhase SampleManagerPhase => sampleDrawerPresentation.Phase;
    internal bool ShouldRestoreDrawerFocus(bool presentationCanceled, bool focusInsideDrawer) =>
        ShouldRestoreDrawerFocus(IsSampleManagerVisible ? sampleDrawerPresentation : InstrumentalDrawerPresentation, presentationCanceled, focusInsideDrawer);
    private bool ShouldRestoreDrawerFocus(OHsShellDrawerPresentation state, bool presentationCanceled, bool focusInsideDrawer) =>
        !presentationCanceled && !IsPopupOverlayVisible && ((!state.IsBottom && state.DockedPage is null) || focusInsideDrawer);

    private void SetDrawerPhase(OHsShellDrawerPresentation state, OHsSampleDrawerPhase phase)
    {
        state.Phase = phase;
        var samplesVisible = sampleDrawerPresentation.Phase != OHsSampleDrawerPhase.Closed;
        var bottomVisible = InstrumentalDrawerPresentation.Phase != OHsSampleDrawerPhase.Closed;
        SetValue(IsSampleManagerVisiblePropertyKey, samplesVisible);
        SetValue(IsBottomDrawerPropertyKey, bottomVisible);
        SetValue(IsAnyDrawerVisiblePropertyKey, samplesVisible || bottomVisible);
        SetValue(IsBlockingDrawerVisiblePropertyKey, samplesVisible && sampleDrawerPresentation.DockedPage is null);
    }

    private async void OnSampleManagerBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (sampleDrawerPresentation.DockedPage is not null) return;
        e.Handled = true;
        if (SampleManagerHost.Content is not UserControl drawer || drawer is not IOHsShellDrawer) return;
        try { await RequestSampleManagerCloseAsync(drawer); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Sample drawer close failed: {0}", error); DismissSampleManager(drawer); }
    }

    internal Task<bool> RequestSampleManagerCloseAsync(UserControl drawer)
    {
        var state = PresentationFor(drawer);
        if (state is null || IsPopupOverlayVisible) return Task.FromResult(false);
        if (state.Phase != OHsSampleDrawerPhase.Closing &&
            (drawer is not IOHsShellDrawer lifecycle || !lifecycle.CanDismiss || !lifecycle.TryPrepareClose())) return Task.FromResult(false);
        return state.Close?.Invoke() ?? Task.FromResult(false);
    }

    private OHsShellDrawerPresentation? PresentationFor(UserControl drawer) =>
        ReferenceEquals(sampleDrawerPresentation.Host.Content, drawer) ? sampleDrawerPresentation :
        ReferenceEquals(InstrumentalDrawerPresentation.Host.Content, drawer) ? InstrumentalDrawerPresentation : null;

    internal Task PresentSampleManagerAsync(ManageSamplesDrawer drawer, CancellationToken token,
        Func<bool,CancellationToken,Task>? transition=null) => PresentDrawerAsync(drawer,token,transition);
    internal Task PresentDrawerAsync<T>(T drawer, CancellationToken token,
        Func<bool, CancellationToken, Task>? transition = null, bool bottomNonModal = false)
        where T:UserControl,IOHsShellDrawer
    {
        var state = bottomNonModal ? InstrumentalDrawerPresentation : sampleDrawerPresentation;
        if (state.Completion is not null)
            return Task.FromException(new InvalidOperationException("The drawer is already open."));
        return state.Presentation = PresentDrawerCoreAsync(state, drawer, token, transition, bottomNonModal);
    }

    private async Task PresentDrawerCoreAsync<T>(OHsShellDrawerPresentation state, T drawer, CancellationToken token,
        Func<bool, CancellationToken, Task>? transition, bool bottomNonModal)
        where T:UserControl,IOHsShellDrawer
    {
        if (token.IsCancellationRequested || closed) return;
        state.ClearFocusRestore?.Invoke();
        var invoker = Keyboard.FocusedElement as FrameworkElement ??
            (PageHost.Content as WorkstationPage)?.Toolbar.FindName("ManageSamplesButton") as FrameworkElement;
        using var presentation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        using var entranceCancellation = CancellationTokenSource.CreateLinkedTokenSource(presentation.Token);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Completion = completion;
        state.Cancellation = presentation;
        var animate = transition ?? ((opening, cancel) => RunDrawerAnimationAsync(state, opening, SystemParameters.ClientAreaAnimation, cancel));
        Task entrance = Task.CompletedTask;
        Task<bool> closing = Task.FromResult(false);
        var ownedFocusBeforeClose = false;
        Task<bool> CloseNormally()
        {
            if (state.Phase == OHsSampleDrawerPhase.Closing) return closing;
            ownedFocusBeforeClose = drawer.IsKeyboardFocusWithin;
            SetDrawerPhase(state, OHsSampleDrawerPhase.Closing);
            drawer.IsEnabled = false;
            entranceCancellation.Cancel();
            return closing = FinishCloseAsync();
        }
        async Task<bool> FinishCloseAsync()
        {
            try
            {
                try { await entrance; }
                catch (OperationCanceledException) when (entranceCancellation.IsCancellationRequested) { }
                if (presentation.IsCancellationRequested) return false;
                await AwaitSampleManagerTransitionAsync(animate, false, presentation.Token);
            }
            catch (OperationCanceledException) when (presentation.IsCancellationRequested) { }
            catch (Exception error) { System.Diagnostics.Trace.TraceError("Sample drawer exit failed: {0}", error); }
            finally { completion.TrySetResult(); }
            return true;
        }
        state.Close = CloseNormally;
        async void OnCloseRequested(object? sender, EventArgs args)
        {
            try { await RequestSampleManagerCloseAsync(drawer); }
            catch (Exception error) { System.Diagnostics.Trace.TraceError("Sample drawer close failed: {0}", error); DismissSampleManager(drawer); }
        }
        void OnLoaded(object sender, RoutedEventArgs args)
        {
            if (state.Phase != OHsSampleDrawerPhase.Closing) drawer.FocusInitialAction();
        }
        var workstation = PageHost.Content as WorkstationPage;
        Panel? motionParent = null;
        var motionRow = Grid.GetRow(state.MotionHost);
        var instrumentDrawer = drawer is OHsInstrumentalArrangementDrawer or OHsKeysMappingDrawer;
        OHsInstrumentalArrangementDrawer? piano = drawer as OHsInstrumentalArrangementDrawer;
        Action<double>? scrollPiano = null;
        EventHandler? alignPiano = null;
        EventHandler? expandPiano = null;
        using var cancellation = presentation.Token.Register(() => completion.TrySetResult());
        try
        {
            drawer.CloseRequested += OnCloseRequested;
            drawer.Loaded += OnLoaded;
            CancelBackgroundDrag();
            state.DockedPage = workstation;
            state.IsBottom = bottomNonModal;
            SetDrawerPhase(state, OHsSampleDrawerPhase.Opening);
            drawer.IsEnabled = true;
            state.Host.Tag = workstation is not null || bottomNonModal;
            state.Host.Style = (Style)FindResource(drawer is OHsKeysMappingDrawer
                ? "OHsMainWindowKeysMappingDrawerHostStyle" : "OHsMainWindowShellDrawerHostStyle");
            state.Host.Content = drawer;
            if (workstation is not null)
            {
                state.DockedPage = workstation;
                motionParent = (Panel)state.MotionHost.Parent;
                motionParent.Children.Remove(state.MotionHost);
                Grid.SetRow(state.MotionHost, 0);
                workstation.AttachDrawerHost(state.MotionHost, bottomNonModal
                    ? drawer is OHsKeysMappingDrawer ? OHsWorkstationDrawerKind.KeysMapping : OHsWorkstationDrawerKind.Arrangement
                    : OHsWorkstationDrawerKind.Samples);
            }
            // Content attachment sets local flow direction; bind after attachment so shell changes remain live.
            if (workstation is not null || instrumentDrawer)
                drawer.SetBinding(FlowDirectionProperty, new Binding(nameof(FlowDirection)) { Source = this, Mode = BindingMode.OneWay });
            if (piano is not null && PageHost.Content is WorkstationPage page)
            {
                piano.SetBinding(OHsInstrumentalArrangementDrawer.IsExpandedProperty,
                    new Binding(nameof(page.IsPianoRollExpanded)) { Source = page, Mode = BindingMode.OneWay });
                expandPiano = (_, _) =>
                {
                    if (!ReferenceEquals(state.Host.Content, piano) || !ReferenceEquals(PageHost.Content, page)) return;
                    page.TogglePianoRollExpansion();
                    piano.FocusExpansionAction();
                };
                piano.ExpansionRequested += expandPiano;
                piano.SetBinding(OHsInstrumentalArrangementDrawer.TimelineContentWidthProperty, new Binding(nameof(page.Timeline.TimelineContentWidth)) { Source = page.Timeline, Mode = BindingMode.OneWay });
                piano.SetBinding(OHsInstrumentalArrangementDrawer.TimelineViewportWidthProperty, new Binding(nameof(page.Timeline.TimelineViewportWidth)) { Source = page.Timeline, Mode = BindingMode.OneWay });
                piano.SetBinding(OHsInstrumentalArrangementDrawer.TimelineHorizontalOffsetProperty, new Binding(nameof(page.Timeline.TimelineHorizontalOffset)) { Source = page.Timeline, Mode = BindingMode.OneWay });
                scrollPiano = offset => { if (ReferenceEquals(state.Host.Content, piano) && ReferenceEquals(PageHost.Content, page)) page.Timeline.ScrollFromUser(offset); };
                piano.HorizontalOffsetRequested += scrollPiano;
                alignPiano = (_, _) =>
                {
                    if (ReferenceEquals(state.Host.Content, piano) && ReferenceEquals(PageHost.Content, page))
                        piano.AlignTimelineViewport(page.Timeline.GetTimeViewportBounds(piano));
                };
                piano.LayoutUpdated += alignPiano;
                alignPiano(null, EventArgs.Empty);
                piano.Attach();
            }
            SystemParameters.StaticPropertyChanged += OnSampleManagerAnimationSettingChanged;
            entrance = AwaitSampleManagerTransitionAsync(animate, true, entranceCancellation.Token);
            try { await entrance; }
            catch (OperationCanceledException) when (entranceCancellation.IsCancellationRequested) { }
            await Dispatcher.InvokeAsync(() =>
            {
                if (!presentation.IsCancellationRequested && state.Phase == OHsSampleDrawerPhase.Opening)
                {
                    state.DockedPage?.SettleDrawerHostAnimation(state.MotionHost, true);
                    SetDrawerPhase(state, OHsSampleDrawerPhase.Open);
                }
            });
            await completion.Task;
        }
        finally
        {
            var presentationCanceled = presentation.IsCancellationRequested;
            presentation.Cancel();
            try { await closing; }
            catch (OperationCanceledException) when (presentation.IsCancellationRequested) { }
            await Dispatcher.InvokeAsync(() =>
            {
                var focusInside = IsDrawerOrigin(Keyboard.FocusedElement as DependencyObject, drawer) ||
                    ownedFocusBeforeClose && (Keyboard.FocusedElement is null || ReferenceEquals(Keyboard.FocusedElement, this));
                var restoreFocus = ShouldRestoreDrawerFocus(state, presentationCanceled, focusInside);
                SystemParameters.StaticPropertyChanged -= OnSampleManagerAnimationSettingChanged;
                drawer.CloseRequested -= OnCloseRequested;
                drawer.Loaded -= OnLoaded;
                if (workstation is not null || instrumentDrawer) BindingOperations.ClearBinding(drawer, FlowDirectionProperty);
                if (piano is not null)
                {
                    piano.Detach();
                    if (expandPiano is not null) piano.ExpansionRequested -= expandPiano;
                    BindingOperations.ClearBinding(piano, OHsInstrumentalArrangementDrawer.IsExpandedProperty);
                    if (alignPiano is not null) piano.LayoutUpdated -= alignPiano;
                    if (scrollPiano is not null) piano.HorizontalOffsetRequested -= scrollPiano;
                    BindingOperations.ClearBinding(piano, OHsInstrumentalArrangementDrawer.TimelineContentWidthProperty);
                    BindingOperations.ClearBinding(piano, OHsInstrumentalArrangementDrawer.TimelineViewportWidthProperty);
                    BindingOperations.ClearBinding(piano, OHsInstrumentalArrangementDrawer.TimelineHorizontalOffsetProperty);
                }
                if (ReferenceEquals(state.Completion, completion)) ResetDrawerAnimation(state);
                if (ReferenceEquals(state.Host.Content, drawer))
                {
                    state.Host.Content = null;
                    state.Host.Style = (Style)FindResource("OHsMainWindowShellDrawerHostStyle");
                }
                if (ReferenceEquals(state.Completion, completion))
                {
                    workstation?.DetachDrawerHost(state.MotionHost);
                    if (motionParent is not null)
                    {
                        Grid.SetRow(state.MotionHost, motionRow);
                        if (state.MotionHost.Parent is null) motionParent.Children.Add(state.MotionHost);
                    }
                    state.DockedPage = null;
                    state.Completion = null;
                    state.Cancellation = null;
                    state.Close = null;
                    SetDrawerPhase(state, OHsSampleDrawerPhase.Closed);
                    state.IsBottom = false;
                }
                drawer.IsEnabled = true;
                if (!closed && !closeRequested && state.Host.Content is null && restoreFocus && invoker is not null)
                    RestoreSampleManagerFocus(state, invoker, workstation is not null || bottomNonModal, drawer);
            });
        }
    }

    internal void DismissSampleManager(UserControl drawer)
    {
        if (PresentationFor(drawer) is not { } state) return;
        state.Cancellation?.Cancel();
        state.Host.Content = null;
        state.Completion?.TrySetResult();
    }

    private async Task SettleShellDrawersAsync(WorkstationPage? page = null)
    {
        var owners = new[] { sampleDrawerPresentation, InstrumentalDrawerPresentation };
        foreach (var state in owners)
            if ((page is null || ReferenceEquals(state.DockedPage, page)) && state.Host.Content is UserControl drawer)
                DismissSampleManager(drawer);
        await Task.WhenAll(owners.Where(state => page is null || ReferenceEquals(state.DockedPage, page)).Select(state => state.Presentation));
    }

    private async Task AwaitSampleManagerTransitionAsync(Func<bool, CancellationToken, Task> animate, bool opening, CancellationToken token)
    {
        var animation = await Dispatcher.InvokeAsync(() => animate(opening, token));
        try { await animation.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Substituted transitions may ignore cancellation; observe any late failure without delaying teardown.
            if (!animation.IsCompleted)
                _ = animation.ContinueWith(task => System.Diagnostics.Trace.TraceError("Cancelled drawer transition failed: {0}", task.Exception),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }

    private bool IsDrawerOrigin(DependencyObject? origin, UserControl? drawer = null)
    {
        while (origin is not null)
        {
            if (drawer is not null ? ReferenceEquals(origin, drawer) :
                ReferenceEquals(origin, sampleDrawerPresentation.Host.Content) || ReferenceEquals(origin, InstrumentalDrawerPresentation.Host.Content)) return true;
            origin = LogicalTreeHelper.GetParent(origin) ?? (origin is System.Windows.Media.Visual
                ? System.Windows.Media.VisualTreeHelper.GetParent(origin)
                : origin is FrameworkContentElement content ? content.Parent : null);
        }
        return false;
    }
    private void RestoreSampleManagerFocus(OHsShellDrawerPresentation state, FrameworkElement invoker, bool bottomNonModal, UserControl drawer)
    {
        bool FocusStillOwned() => !bottomNonModal || Keyboard.FocusedElement is null || ReferenceEquals(Keyboard.FocusedElement, this) ||
            IsDrawerOrigin(Keyboard.FocusedElement as DependencyObject, drawer);
        if (!FocusStillOwned()) return;
        if (!invoker.IsVisible) return;
        if (invoker.IsEnabled) { invoker.Focus(); return; }
        void OnEnabledChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            if (!invoker.IsEnabled) return;
            state.ClearFocusRestore?.Invoke();
            if (invoker.IsVisible && !closed && FocusStillOwned()) invoker.Focus();
        }
        void OnUnloaded(object sender, RoutedEventArgs args) => state.ClearFocusRestore?.Invoke();
        state.ClearFocusRestore = () =>
        {
            invoker.IsEnabledChanged -= OnEnabledChanged;
            invoker.Unloaded -= OnUnloaded;
            state.ClearFocusRestore = null;
        };
        invoker.IsEnabledChanged += OnEnabledChanged;
        invoker.Unloaded += OnUnloaded;
    }
}
