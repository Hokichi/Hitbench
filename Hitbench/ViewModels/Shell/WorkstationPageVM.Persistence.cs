using CommunityToolkit.Mvvm.Input;
using Hitbench.Core.Entities;
using Hitbench.Services.ProjectFiles;

namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private DateTimeOffset latestRequestedLastChanged;
    private (string Path, ProjectData Project, int Generation, Func<string> Json, DateTimeOffset Timestamp)? pendingSnapshot;
    private bool savingSnapshots;
    private bool forceSnapshots;
    private CancellationTokenSource? snapshotDelay;
    private readonly object snapshotDelayGate = new();
    private int snapshotRequest;
    internal long SaveSnapshotCount { get; private set; }

    [RelayCommand]
    private void RetrySave() => QueueSave(false);

    private void QueueSave(bool isChange = true)
    {
        if (projectFiles is null || projectPath is null || Project is null) return;
        if (isChange)
        {
            var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
            if (now > latestRequestedLastChanged) latestRequestedLastChanged = now;
        }
        var project = Project;
        var timestamp = latestRequestedLastChanged;
        snapshotRequest++;
        pendingSnapshot = (projectPath, project, loadGeneration, () => { SaveSnapshotCount++; return ProjectDataSerializer.Serialize(project, timestamp); }, timestamp);
        pendingProjectWrites = 1;
        CancelSnapshotDelay();
        if (!savingSnapshots)
        {
            savingSnapshots = true;
            saveTail = SavePendingSnapshotsAsync();
        }
        PublishInstrumentSaveState(); PublishControlPointsSnapshot();
    }

    private async Task SavePendingSnapshotsAsync()
    {
        try
        {
            while (pendingSnapshot is not null)
            {
                var clock = timeProvider ?? TimeProvider.System;
                var started = clock.GetTimestamp();
                while (!forceSnapshots)
                {
                    var request = snapshotRequest;
                    using var delay = new CancellationTokenSource();
                    lock (snapshotDelayGate) snapshotDelay = delay;
                    try { await (saveDelay ?? Task.Delay)(TimeSpan.FromMilliseconds(Math.Max(0, Math.Min(150, 1000 - clock.GetElapsedTime(started).TotalMilliseconds))), delay.Token); }
                    catch (OperationCanceledException) when (delay.IsCancellationRequested) { }
                    finally { lock (snapshotDelayGate) snapshotDelay = null; }
                    if (!delay.IsCancellationRequested && request == snapshotRequest || clock.GetElapsedTime(started).TotalMilliseconds >= 1000) break;
                }
                if (pendingSnapshot is not { } snapshot) break;
                pendingSnapshot = null;
                try
                {
                    string? json = null;
                    await uiScheduler.InvokeAsync(() => json = snapshot.Json(), CancellationToken.None);
                    await projectFiles!.SaveAsync(snapshot.Path, json!, CancellationToken.None);
                    await uiScheduler.InvokeAsync(() =>
                    {
                        snapshot.Project.LastChanged = snapshot.Timestamp;
                        if (projectPath != snapshot.Path || !ReferenceEquals(Project, snapshot.Project)) return;
                        lastSaveError = null;
                        if (IsCurrentProject(snapshot.Path, snapshot.Project, snapshot.Generation)) SaveError = null;
                    }, CancellationToken.None);
                }
                catch (Exception error)
                {
                    await uiScheduler.InvokeAsync(() =>
                    {
                        if (projectPath != snapshot.Path || !ReferenceEquals(Project, snapshot.Project)) return;
                        lastSaveError = error;
                        if (IsCurrentProject(snapshot.Path, snapshot.Project, snapshot.Generation)) SaveError = error.Message;
                    }, CancellationToken.None);
                }
            }
        }
        finally
        {
            await uiScheduler.InvokeAsync(() =>
            {
                savingSnapshots = false;
                pendingProjectWrites = pendingSnapshot is null ? 0 : 1;
                if (pendingSnapshot is not null)
                {
                    savingSnapshots = true;
                    saveTail = SavePendingSnapshotsAsync();
                }
                else forceSnapshots = false;
                PublishInstrumentSaveState(); PublishControlPointsSnapshot();
            }, CancellationToken.None);
        }
    }

    private async Task AwaitProjectSavesAsync()
    {
        forceSnapshots = true;
        CancelSnapshotDelay();
        Task pending;
        do { pending = saveTail; await pending; }
        while (!ReferenceEquals(pending, saveTail));
        if (!savingSnapshots) forceSnapshots = false;
    }

    private void CancelSnapshotDelay()
    {
        lock (snapshotDelayGate) snapshotDelay?.Cancel();
    }
}
