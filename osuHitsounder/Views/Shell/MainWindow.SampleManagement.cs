using System.Windows;
using System.Windows.Input;
using osuHitsounder.Views.Pages;
using osuHitsounder.Views.Popups;

namespace osuHitsounder;

public partial class MainWindow
{
    private TaskCompletionSource? sampleManagerCompletion;
    private Action? clearSampleFocusRestore;

    internal async Task PresentSampleManagerAsync(ManageSamplesDrawer drawer, CancellationToken token)
    {
        if (SampleManagerHost.Content is not null) throw new InvalidOperationException("The sample manager is already open.");
        if (token.IsCancellationRequested || closed) return;
        clearSampleFocusRestore?.Invoke();
        var invoker = Keyboard.FocusedElement as FrameworkElement ??
            (PageHost.Content as WorkstationPage)?.FindName("ManageSamplesButton") as FrameworkElement;
        using var presentation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sampleManagerCompletion = completion;
        void OnCloseRequested(object? sender, EventArgs args) => DismissSampleManager(drawer);
        void OnLoaded(object sender, RoutedEventArgs args) => drawer.FocusInitialAction();
        drawer.CloseRequested += OnCloseRequested;
        drawer.Loaded += OnLoaded;
        SampleManagerHost.Content = drawer;
        using var cancellation = presentation.Token.Register(() => completion.TrySetResult());
        try { await completion.Task; }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                drawer.CloseRequested -= OnCloseRequested;
                drawer.Loaded -= OnLoaded;
                if (ReferenceEquals(SampleManagerHost.Content, drawer)) SampleManagerHost.Content = null;
                if (ReferenceEquals(sampleManagerCompletion, completion)) sampleManagerCompletion = null;
                if (!closed && !closeRequested && SampleManagerHost.Content is null && !presentation.IsCancellationRequested && invoker is not null)
                    RestoreSampleManagerFocus(invoker);
            });
        }
    }

    internal void DismissSampleManager(ManageSamplesDrawer drawer)
    {
        if (!ReferenceEquals(SampleManagerHost.Content, drawer)) return;
        SampleManagerHost.Content = null;
        sampleManagerCompletion?.TrySetResult();
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
