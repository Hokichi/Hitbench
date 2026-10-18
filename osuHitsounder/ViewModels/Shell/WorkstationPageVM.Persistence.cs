using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services.ProjectFiles;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private DateTimeOffset latestRequestedLastChanged;

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
        var json = ProjectDataSerializer.Serialize(Project, latestRequestedLastChanged);
        var previous = saveTail;
        pendingProjectWrites++;
        PublishInstrumentSaveState();
        PublishControlPointsSnapshot();
        saveTail = SaveSnapshotAsync(previous, projectPath, Project, json, latestRequestedLastChanged);
    }

    private async Task SaveSnapshotAsync(Task previous, string path, ProjectData project, string json, DateTimeOffset snapshotTimestamp)
    {
        await previous;
        try
        {
            await projectFiles!.SaveAsync(path, json, CancellationToken.None);
            await uiScheduler.InvokeAsync(() =>
            {
                project.LastChanged = snapshotTimestamp;
                lastSaveError = null;
                SaveError = null;
            }, CancellationToken.None);
        }
        catch (Exception error)
        {
            lastSaveError = error;
            SaveError = error.Message;
        }
        finally { pendingProjectWrites--; PublishInstrumentSaveState(); PublishControlPointsSnapshot(); }
    }
}
