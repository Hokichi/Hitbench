using Hitbench.Resources.Helpers;

namespace Hitbench.Views.Pages;

public partial class OHsWorkstationTimeline
{
    private TimeSpan? pendingSeek;
    private double? requestedOffset;
    private bool navigating;
    private int navigationGeneration;
    public Task NavigationCompletion { get; private set; } = Task.CompletedTask;
    private double UserHorizontalOffset => requestedOffset ?? (viewModel.IsStaticMarker && viewModel.Waveform is { } waveform
        ? OHsTimelineViewport.GetCenteredOffset(viewModel.Position, waveform.Duration, TimelineContentWidth, TimelineViewportWidth) : TimelineHorizontalOffset);

    private void RequestSeek(TimeSpan position)
    {
        if (deactivationStarted) return;
        pendingSeek = position;
        if (!navigating)
        {
            navigating = true;
            var generation = navigationGeneration;
            NavigationCompletion = Dispatcher.InvokeAsync(() => RunNavigationAsync(generation)).Task.Unwrap();
        }
    }

    private async Task RunNavigationAsync(int generation)
    {
        navigating = true;
        var owner = viewModel;
        try
        {
            while (generation == navigationGeneration && !deactivationStarted && pendingSeek is { } target)
            {
                pendingSeek = null;
                if (owner.SeekCommand.IsRunning && owner.SeekCommand.ExecutionTask is { } current) await current;
                if (generation != navigationGeneration || deactivationStarted) break;
                if (owner.SeekCommand.CanExecute(target)) await owner.SeekCommand.ExecuteAsync(target);
            }
        }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Timeline navigation failed: {0}", error); }
        finally
        {
            navigating = false;
            if (generation != navigationGeneration && pendingSeek is not null && !deactivationStarted)
            {
                await RunNavigationAsync(navigationGeneration);
            }
            if (generation == navigationGeneration)
            {
                if (!deactivationStarted && owner.IsStaticMarker) CenterPlaybackPosition();
            }
        }
    }

    private void ResetNavigation()
    {
        navigationGeneration++;
        pendingSeek = null;
        requestedOffset = null;
    }

    public Task CancelNavigationAsync() { ResetNavigation(); return NavigationCompletion; }

    private void SeekViewportPosition(double viewportX, double offset)
    {
        if (viewModel.Waveform is { } waveform &&
            OHsTimelineViewport.GetPositionAt(viewportX, offset, TimelineContentWidth, waveform.Duration) is { } position)
            RequestSeek(position);
    }
}
