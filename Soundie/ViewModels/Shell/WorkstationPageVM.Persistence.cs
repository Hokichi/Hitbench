using CommunityToolkit.Mvvm.Input;
using Soundie.Core.Entities;
using Soundie.Services.ProjectFiles;

namespace Soundie.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private DateTimeOffset latestRequestedLastChanged;
    private (string Path, ProjectData Project, int Generation, string Json, DateTimeOffset Timestamp)? pendingSnapshot;
    private bool savingSnapshots;

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
        pendingSnapshot = (projectPath, Project, loadGeneration, ProjectDataSerializer.Serialize(Project, latestRequestedLastChanged), latestRequestedLastChanged);
        pendingProjectWrites = 1;
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
            while (pendingSnapshot is { } snapshot)
            {
                pendingSnapshot = null;
                try
                {
                    await projectFiles!.SaveAsync(snapshot.Path, snapshot.Json, CancellationToken.None);
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
            savingSnapshots = false; pendingProjectWrites = 0;
            PublishInstrumentSaveState(); PublishControlPointsSnapshot();
        }
    }

    private async Task AwaitProjectSavesAsync()
    {
        Task pending;
        do { pending = saveTail; await pending; }
        while (!ReferenceEquals(pending, saveTail));
    }
}
