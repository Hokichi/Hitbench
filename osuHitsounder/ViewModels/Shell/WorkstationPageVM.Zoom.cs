using osuHitsounder.Core.Entities;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private CancellationTokenSource? zoomCancellation;
    private int? pendingZoom;
    private Task zoomSave = Task.CompletedTask;
    private readonly Func<TimeSpan, CancellationToken, Task> waitForZoom = zoomDelay ?? Task.Delay;

    private bool CanZoomIn() => CanZoom && ZoomFactor < 128;
    private bool CanZoomOut() => CanZoom && ZoomFactor > 8;

    private void RestoreZoom(int value)
    {
        zoomFactor = Math.Clamp(value, 8, 128);
        Project!.WorkstationZoom = (int)zoomFactor;
        OnPropertyChanged(nameof(ZoomFactor));
    }

    private void SetZoom(int value)
    {
        if (!CanZoom) return;
        var next = Math.Clamp(value, 8, 128);
        if (zoomFactor == next) return;
        OnPropertyChanging(nameof(ZoomFactor));
        zoomFactor = next;
        OnPropertyChanged(nameof(ZoomFactor));
        ScheduleZoomSave();
        NotifyCommandState();
        PublishInstrumentTimeline();
    }

    private void ScheduleZoomSave()
    {
        zoomCancellation?.Cancel();
        zoomCancellation?.Dispose();
        zoomCancellation = null;
        pendingZoom = Project is not null && Project.WorkstationZoom != (int)zoomFactor ? (int)zoomFactor : null;
        OnPropertyChanged(nameof(HasPendingSaves));
        if (pendingZoom is null || Project is null || projectPath is null) return;
        zoomCancellation = new CancellationTokenSource();
        zoomSave = DebounceZoomSaveAsync(Project, projectPath, loadGeneration, zoomCancellation.Token);
    }

    private async Task DebounceZoomSaveAsync(ProjectData project, string path, int generation, CancellationToken token)
    {
        try
        {
            await waitForZoom(TimeSpan.FromMilliseconds(300), token);
            await uiScheduler.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested && IsCurrentProject(path, project, generation)) CommitPendingZoom();
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            await uiScheduler.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested && IsCurrentProject(path, project, generation)) ErrorMessage = error.Message;
            }, CancellationToken.None);
        }
    }

    private bool CommitPendingZoom(bool queueSave = true)
    {
        zoomCancellation?.Cancel();
        zoomCancellation?.Dispose();
        zoomCancellation = null;
        var value = pendingZoom;
        pendingZoom = null;
        OnPropertyChanged(nameof(HasPendingSaves));
        if (value is null || Project is null || Project.WorkstationZoom == value.Value) return false;
        Project.WorkstationZoom = value.Value;
        if (queueSave) QueueSave();
        return true;
    }
}
