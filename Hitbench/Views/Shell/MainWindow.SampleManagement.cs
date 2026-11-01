using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Data;
using Hitbench.Resources.CustomControls;
using Hitbench.Views.Pages;
using Hitbench.Views.Popups;
using Hitbench.Resources.DataModels;

namespace Hitbench;

public partial class MainWindow
{
    private TaskCompletionSource? sampleManagerCompletion;
    private CancellationTokenSource? sampleManagerCancellation;
    private Func<Task<bool>>? closeSampleManager;
    private Action? clearSampleFocusRestore;
    private WorkstationPage? dockedDrawerPage;
    private Task sampleManagerPresentation = Task.CompletedTask;
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
    internal bool ShouldRestoreDrawerFocus(bool presentationCanceled, bool focusInsideDrawer) =>
        !presentationCanceled && ((!IsBottomDrawer && dockedDrawerPage is null) || focusInsideDrawer);
    internal OHsSampleDrawerPhase SampleManagerPhase { get; private set; }

    private void SetSampleManagerPhase(OHsSampleDrawerPhase phase)
    {
        SampleManagerPhase = phase;
        SetValue(IsSampleManagerVisiblePropertyKey, phase != OHsSampleDrawerPhase.Closed);
        SetValue(IsBlockingDrawerVisiblePropertyKey, phase != OHsSampleDrawerPhase.Closed && !IsBottomDrawer && dockedDrawerPage is null);
    }

    private async void OnSampleManagerBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (IsBottomDrawer || dockedDrawerPage is not null) return;
        e.Handled = true;
        if (SampleManagerHost.Content is not UserControl drawer || drawer is not IOHsShellDrawer) return;
        try { await RequestSampleManagerCloseAsync(drawer); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Sample drawer close failed: {0}", error); DismissSampleManager(drawer); }
    }

    internal Task<bool> RequestSampleManagerCloseAsync(UserControl drawer)
    {
        if (!ReferenceEquals(SampleManagerHost.Content, drawer) || IsPopupOverlayVisible) return Task.FromResult(false);
        if (SampleManagerPhase != OHsSampleDrawerPhase.Closing && (drawer is not IOHsShellDrawer lifecycle || !lifecycle.CanDismiss || !lifecycle.TryPrepareClose())) return Task.FromResult(false);
        return closeSampleManager?.Invoke() ?? Task.FromResult(false);
    }

    internal Task PresentSampleManagerAsync(ManageSamplesDrawer drawer, CancellationToken token,
        Func<bool,CancellationToken,Task>? transition=null) => PresentDrawerAsync(drawer,token,transition);
    internal Task PresentDrawerAsync<T>(T drawer, CancellationToken token,
        Func<bool, CancellationToken, Task>? transition = null, bool bottomNonModal = false)
        where T:UserControl,IOHsShellDrawer
    {
        if (sampleManagerCompletion is not null)
            return Task.FromException(new InvalidOperationException("The sample manager is already open."));
        return sampleManagerPresentation = PresentDrawerCoreAsync(drawer, token, transition, bottomNonModal);
    }

    private async Task PresentDrawerCoreAsync<T>(T drawer, CancellationToken token,
        Func<bool, CancellationToken, Task>? transition, bool bottomNonModal)
        where T:UserControl,IOHsShellDrawer
    {
        if (token.IsCancellationRequested || closed) return;
        clearSampleFocusRestore?.Invoke();
        var invoker = Keyboard.FocusedElement as FrameworkElement ??
            (PageHost.Content as WorkstationPage)?.Toolbar.FindName("ManageSamplesButton") as FrameworkElement;
        using var presentation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        using var entranceCancellation = CancellationTokenSource.CreateLinkedTokenSource(presentation.Token);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sampleManagerCompletion = completion;
        sampleManagerCancellation = presentation;
        var animate = transition ?? AnimateSampleManagerAsync;
        Task entrance = Task.CompletedTask;
        Task<bool> closing = Task.FromResult(false);
        var ownedFocusBeforeClose = false;
        Task<bool> CloseNormally()
        {
            if (SampleManagerPhase == OHsSampleDrawerPhase.Closing) return closing;
            ownedFocusBeforeClose = drawer.IsKeyboardFocusWithin;
            SetSampleManagerPhase(OHsSampleDrawerPhase.Closing);
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
        closeSampleManager = CloseNormally;
        async void OnCloseRequested(object? sender, EventArgs args)
        {
            try { await RequestSampleManagerCloseAsync(drawer); }
            catch (Exception error) { System.Diagnostics.Trace.TraceError("Sample drawer close failed: {0}", error); DismissSampleManager(drawer); }
        }
        void OnLoaded(object sender, RoutedEventArgs args)
        {
            if (SampleManagerPhase != OHsSampleDrawerPhase.Closing) drawer.FocusInitialAction();
        }
        var workstation = PageHost.Content as WorkstationPage;
        Panel? motionParent = null;
        var motionRow = Grid.GetRow(SampleManagerMotionHost);
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
            dockedDrawerPage = workstation;
            SetValue(IsBottomDrawerPropertyKey, bottomNonModal);
            SetSampleManagerPhase(OHsSampleDrawerPhase.Opening);
            drawer.IsEnabled = true;
            SampleManagerHost.Tag = workstation is not null || bottomNonModal;
            SampleManagerHost.Style = (Style)FindResource(drawer is OHsKeysMappingDrawer
                ? "OHsMainWindowKeysMappingDrawerHostStyle" : "OHsMainWindowShellDrawerHostStyle");
            SampleManagerHost.Content = drawer;
            if (workstation is not null)
            {
                dockedDrawerPage = workstation;
                motionParent = (Panel)SampleManagerMotionHost.Parent;
                motionParent.Children.Remove(SampleManagerMotionHost);
                Grid.SetRow(SampleManagerMotionHost, 0);
                workstation.AttachDrawerHost(SampleManagerMotionHost, bottomNonModal
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
                    if (!ReferenceEquals(SampleManagerHost.Content, piano) || !ReferenceEquals(PageHost.Content, page)) return;
                    page.TogglePianoRollExpansion();
                    piano.FocusExpansionAction();
                };
                piano.ExpansionRequested += expandPiano;
                piano.SetBinding(OHsInstrumentalArrangementDrawer.TimelineContentWidthProperty, new Binding(nameof(page.Timeline.TimelineContentWidth)) { Source = page.Timeline, Mode = BindingMode.OneWay });
                piano.SetBinding(OHsInstrumentalArrangementDrawer.TimelineViewportWidthProperty, new Binding(nameof(page.Timeline.TimelineViewportWidth)) { Source = page.Timeline, Mode = BindingMode.OneWay });
                piano.SetBinding(OHsInstrumentalArrangementDrawer.TimelineHorizontalOffsetProperty, new Binding(nameof(page.Timeline.TimelineHorizontalOffset)) { Source = page.Timeline, Mode = BindingMode.OneWay });
                scrollPiano = offset => { if (ReferenceEquals(SampleManagerHost.Content, piano) && ReferenceEquals(PageHost.Content, page)) page.Timeline.ScrollFromUser(offset); };
                piano.HorizontalOffsetRequested += scrollPiano;
                alignPiano = (_, _) =>
                {
                    if (ReferenceEquals(SampleManagerHost.Content, piano) && ReferenceEquals(PageHost.Content, page))
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
                if (!presentation.IsCancellationRequested && SampleManagerPhase == OHsSampleDrawerPhase.Opening)
                {
                    dockedDrawerPage?.SettleDrawerHostAnimation(true);
                    SetSampleManagerPhase(OHsSampleDrawerPhase.Open);
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
                var focusInside = IsDrawerOrigin(Keyboard.FocusedElement as DependencyObject) ||
                    ownedFocusBeforeClose && (Keyboard.FocusedElement is null || ReferenceEquals(Keyboard.FocusedElement, this));
                var restoreFocus = ShouldRestoreDrawerFocus(presentationCanceled, focusInside);
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
                if (ReferenceEquals(sampleManagerCompletion, completion)) ResetSampleManagerAnimation();
                if (ReferenceEquals(SampleManagerHost.Content, drawer))
                {
                    SampleManagerHost.Content = null;
                    SampleManagerHost.Style = (Style)FindResource("OHsMainWindowShellDrawerHostStyle");
                }
                if (ReferenceEquals(sampleManagerCompletion, completion))
                {
                    workstation?.DetachDrawerHost(SampleManagerMotionHost);
                    if (motionParent is not null)
                    {
                        Grid.SetRow(SampleManagerMotionHost, motionRow);
                        if (SampleManagerMotionHost.Parent is null) motionParent.Children.Add(SampleManagerMotionHost);
                    }
                    dockedDrawerPage = null;
                    sampleManagerCompletion = null;
                    sampleManagerCancellation = null;
                    closeSampleManager = null;
                    SetSampleManagerPhase(OHsSampleDrawerPhase.Closed);
                    SetValue(IsBottomDrawerPropertyKey, false);
                }
                drawer.IsEnabled = true;
                if (!closed && !closeRequested && SampleManagerHost.Content is null && restoreFocus && invoker is not null)
                    RestoreSampleManagerFocus(invoker, workstation is not null || bottomNonModal, drawer);
            });
        }
    }

    internal void DismissSampleManager(UserControl drawer)
    {
        if (!ReferenceEquals(SampleManagerHost.Content, drawer)) return;
        sampleManagerCancellation?.Cancel();
        SampleManagerHost.Content = null;
        sampleManagerCompletion?.TrySetResult();
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
            if (ReferenceEquals(origin, drawer ?? SampleManagerHost.Content)) return true;
            origin = LogicalTreeHelper.GetParent(origin) ?? (origin is System.Windows.Media.Visual
                ? System.Windows.Media.VisualTreeHelper.GetParent(origin)
                : origin is FrameworkContentElement content ? content.Parent : null);
        }
        return false;
    }
    private void RestoreSampleManagerFocus(FrameworkElement invoker, bool bottomNonModal, UserControl drawer)
    {
        bool FocusStillOwned() => !bottomNonModal || Keyboard.FocusedElement is null || ReferenceEquals(Keyboard.FocusedElement, this) ||
            IsDrawerOrigin(Keyboard.FocusedElement as DependencyObject, drawer);
        if (!FocusStillOwned()) return;
        if (!invoker.IsVisible) return;
        if (invoker.IsEnabled) { invoker.Focus(); return; }
        void OnEnabledChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            if (!invoker.IsEnabled) return;
            clearSampleFocusRestore?.Invoke();
            if (invoker.IsVisible && !closed && FocusStillOwned()) invoker.Focus();
        }
        void OnUnloaded(object sender, RoutedEventArgs args) => clearSampleFocusRestore?.Invoke();
        clearSampleFocusRestore = () =>
        {
            invoker.IsEnabledChanged -= OnEnabledChanged;
            invoker.Unloaded -= OnUnloaded;
            clearSampleFocusRestore = null;
        };
        invoker.IsEnabledChanged += OnEnabledChanged;
        invoker.Unloaded += OnUnloaded;
    }
}
