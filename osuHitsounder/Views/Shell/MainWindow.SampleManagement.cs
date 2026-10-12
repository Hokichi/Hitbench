using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Views.Pages;
using osuHitsounder.Views.Popups;
using osuHitsounder.Resources.DataModels;

namespace osuHitsounder;

public partial class MainWindow
{
    private TaskCompletionSource? sampleManagerCompletion;
    private CancellationTokenSource? sampleManagerCancellation;
    private Func<Task>? closeSampleManager;
    private Action? clearSampleFocusRestore;
    private static readonly DependencyPropertyKey IsSampleManagerVisiblePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsSampleManagerVisible), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsSampleManagerVisibleProperty = IsSampleManagerVisiblePropertyKey.DependencyProperty;
    public bool IsSampleManagerVisible => (bool)GetValue(IsSampleManagerVisibleProperty);
    internal OHsSampleDrawerPhase SampleManagerPhase { get; private set; }

    private void SetSampleManagerPhase(OHsSampleDrawerPhase phase)
    {
        SampleManagerPhase = phase;
        SetValue(IsSampleManagerVisiblePropertyKey, phase != OHsSampleDrawerPhase.Closed);
    }

    private async void OnSampleManagerBackdropClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (SampleManagerHost.Content is not UserControl drawer || drawer is not IOHsShellDrawer) return;
        try { await RequestSampleManagerCloseAsync(drawer); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Sample drawer close failed: {0}", error); DismissSampleManager(drawer); }
    }

    internal Task RequestSampleManagerCloseAsync(UserControl drawer)
    {
        if (!ReferenceEquals(SampleManagerHost.Content, drawer) || IsPopupOverlayVisible) return Task.CompletedTask;
        if (SampleManagerPhase != OHsSampleDrawerPhase.Closing && (drawer is not IOHsShellDrawer lifecycle || !lifecycle.CanDismiss || !lifecycle.TryPrepareClose())) return Task.CompletedTask;
        return closeSampleManager?.Invoke() ?? Task.CompletedTask;
    }

    internal Task PresentSampleManagerAsync(ManageSamplesDrawer drawer, CancellationToken token,
        Func<bool,CancellationToken,Task>? transition=null) => PresentDrawerAsync(drawer,token,transition);
    internal async Task PresentDrawerAsync<T>(T drawer, CancellationToken token,
        Func<bool, CancellationToken, Task>? transition = null)
        where T:UserControl,IOHsShellDrawer
    {
        if (sampleManagerCompletion is not null) throw new InvalidOperationException("The sample manager is already open.");
        if (token.IsCancellationRequested || closed) return;
        clearSampleFocusRestore?.Invoke();
        var invoker = Keyboard.FocusedElement as FrameworkElement ??
            (PageHost.Content as WorkstationPage)?.FindName("ManageSamplesButton") as FrameworkElement;
        using var presentation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        using var entranceCancellation = CancellationTokenSource.CreateLinkedTokenSource(presentation.Token);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sampleManagerCompletion = completion;
        sampleManagerCancellation = presentation;
        var animate = transition ?? AnimateSampleManagerAsync;
        Task entrance = Task.CompletedTask;
        Task closing = Task.CompletedTask;
        Task CloseNormally()
        {
            if (SampleManagerPhase == OHsSampleDrawerPhase.Closing) return closing;
            SetSampleManagerPhase(OHsSampleDrawerPhase.Closing);
            drawer.IsEnabled = false;
            entranceCancellation.Cancel();
            return closing = FinishCloseAsync();
        }
        async Task FinishCloseAsync()
        {
            try
            {
                try { await entrance; }
                catch (OperationCanceledException) when (entranceCancellation.IsCancellationRequested) { }
                if (presentation.IsCancellationRequested) return;
                await AwaitSampleManagerTransitionAsync(animate, false, presentation.Token);
            }
            catch (OperationCanceledException) when (presentation.IsCancellationRequested) { }
            catch (Exception error) { System.Diagnostics.Trace.TraceError("Sample drawer exit failed: {0}", error); }
            finally { completion.TrySetResult(); }
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
        drawer.CloseRequested += OnCloseRequested;
        drawer.Loaded += OnLoaded;
        CancelBackgroundDrag();
        SetSampleManagerPhase(OHsSampleDrawerPhase.Opening);
        drawer.IsEnabled = true;
        SampleManagerHost.Content = drawer;
        SystemParameters.StaticPropertyChanged += OnSampleManagerAnimationSettingChanged;
        using var cancellation = presentation.Token.Register(() => completion.TrySetResult());
        try
        {
            entrance = AwaitSampleManagerTransitionAsync(animate, true, entranceCancellation.Token);
            try { await entrance; }
            catch (OperationCanceledException) when (entranceCancellation.IsCancellationRequested) { }
            await Dispatcher.InvokeAsync(() =>
            {
                if (!presentation.IsCancellationRequested && SampleManagerPhase == OHsSampleDrawerPhase.Opening)
                    SetSampleManagerPhase(OHsSampleDrawerPhase.Open);
            });
            await completion.Task;
        }
        finally
        {
            var restoreFocus = !presentation.IsCancellationRequested;
            presentation.Cancel();
            try { await closing; }
            catch (OperationCanceledException) when (presentation.IsCancellationRequested) { }
            await Dispatcher.InvokeAsync(() =>
            {
                SystemParameters.StaticPropertyChanged -= OnSampleManagerAnimationSettingChanged;
                drawer.CloseRequested -= OnCloseRequested;
                drawer.Loaded -= OnLoaded;
                ResetSampleManagerAnimation();
                if (ReferenceEquals(SampleManagerHost.Content, drawer)) SampleManagerHost.Content = null;
                if (ReferenceEquals(sampleManagerCompletion, completion))
                {
                    sampleManagerCompletion = null;
                    sampleManagerCancellation = null;
                    closeSampleManager = null;
                    SetSampleManagerPhase(OHsSampleDrawerPhase.Closed);
                }
                drawer.IsEnabled = true;
                if (!closed && !closeRequested && SampleManagerHost.Content is null && restoreFocus && invoker is not null)
                    RestoreSampleManagerFocus(invoker);
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

    private void RestoreSampleManagerFocus(FrameworkElement invoker)
    {
        if (!invoker.IsVisible) return;
        if (invoker.IsEnabled) { invoker.Focus(); return; }
        void OnEnabledChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            if (!invoker.IsEnabled) return;
            clearSampleFocusRestore?.Invoke();
            if (invoker.IsVisible && !closed) invoker.Focus();
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
