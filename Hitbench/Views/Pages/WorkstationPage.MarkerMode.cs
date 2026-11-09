using System.Windows;
using System.Windows.Threading;

namespace Hitbench.Views.Pages;

public partial class WorkstationPage
{
    private CancellationTokenSource? markerModeCancellation;
    private static readonly DependencyPropertyKey IsMarkerModeTransitionActivePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsMarkerModeTransitionActive), typeof(bool), typeof(WorkstationPage), new PropertyMetadata(false, (d, _) =>
        {
            var page = (WorkstationPage)d;
            page.RefreshHeaderDimming(); page.RefreshLoadingPresentation();
        }));
    public static readonly DependencyProperty IsMarkerModeTransitionActiveProperty = IsMarkerModeTransitionActivePropertyKey.DependencyProperty;
    public bool IsMarkerModeTransitionActive => (bool)GetValue(IsMarkerModeTransitionActiveProperty);
    internal Func<TimeSpan, CancellationToken, Task> MarkerModeTransitionDelay { get; set; } = Task.Delay;
    internal Task MarkerModeChangeCompletion { get; private set; } = Task.CompletedTask;

    private void OnMarkerModeRequested(bool value) => MarkerModeChangeCompletion = ChangeMarkerModeAsync(value);

    internal Task ChangeMarkerModeAsync(bool isStatic)
    {
        if (IsMarkerModeTransitionActive) return MarkerModeChangeCompletion;
        if (deactivationStarted || !viewModel.CanPlay || IsAudioBusy || isStatic == viewModel.IsStaticMarker)
        { Toolbar.RefreshMarkerModeSelection(); return Task.CompletedTask; }
        return MarkerModeChangeCompletion = ChangeMarkerModeCoreAsync(isStatic);
    }

    private async Task ChangeMarkerModeCoreAsync(bool isStatic)
    {
        using var cancellation = new CancellationTokenSource();
        markerModeCancellation = cancellation;
        var generation = viewModel.LoadGeneration;
        SetValue(IsMarkerModeTransitionActivePropertyKey, true);
        Toolbar.CloseMenus(); CancelNodePointer(); Timeline.Reset();
        try
        {
            await Timeline.CancelNavigationAsync();
            await MarkerModeTransitionDelay(TimeSpan.FromSeconds(1), cancellation.Token).WaitAsync(cancellation.Token);
            if (deactivationStarted || generation != viewModel.LoadGeneration) return;
            viewModel.RefreshPlayback();
            viewModel.SetMarkerMode(isStatic);
            Timeline.ApplyMarkerMode();
            await Dispatcher.InvokeAsync(() =>
            {
                WorkstationRoot.UpdateLayout();
                Timeline.FollowPlaybackPosition();
            }, DispatcherPriority.Loaded, cancellation.Token);
            await Dispatcher.InvokeAsync(() => WorkstationRoot.UpdateLayout(), DispatcherPriority.ContextIdle, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Marker mode transition failed: {0}", error); }
        finally
        {
            if (ReferenceEquals(markerModeCancellation, cancellation))
            {
                markerModeCancellation = null;
                SetValue(IsMarkerModeTransitionActivePropertyKey, false);
                Toolbar.RefreshMarkerModeSelection();
            }
        }
    }
}
