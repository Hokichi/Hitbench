using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private Guid sampleSession = Guid.NewGuid();
    private Task? activeManager;
    private CancellationTokenSource? managerCancellation;
    private Task sampleChangeNotifications = Task.CompletedTask;
    private bool managingSamples;
    public bool CanManageSamples => CanImportSample && sampleManagementDialog is not null && !managingSamples;
    public TrackSampleAvailability GetSampleAvailability(Track track) => track.SelectedSampleFileName is null
        ? TrackSampleAvailability.Unselected : sampleFiles is null || availableSamples.Any(n => string.Equals(n, track.SelectedSampleFileName, StringComparison.OrdinalIgnoreCase))
            ? TrackSampleAvailability.Available : TrackSampleAvailability.Missing;
    public bool CanEditTrackGrid(Track track) => tracks.Contains(track) && (sampleFiles is null || GetSampleAvailability(track) == TrackSampleAvailability.Available);
    [RelayCommand(CanExecute = nameof(CanManageSamples))]
    private Task ManageSamplesAsync()
    {
        if (!CanManageSamples || projectPath is null) return Task.CompletedTask;
        managingSamples = true; NotifyEditingState();
        managerCancellation?.Dispose(); managerCancellation = CancellationTokenSource.CreateLinkedTokenSource(loadCancellation?.Token ?? CancellationToken.None);
        activeManager = ManageSamplesCoreAsync(new(projectPath, Project?.BeatmapPath, sampleSession), managerCancellation.Token);
        return activeManager;
    }
    private async Task ManageSamplesCoreAsync(SampleManagementContext context, CancellationToken token)
    {
        try
        {
            await sampleManagementDialog!.ShowAsync(context, token); await sampleChangeNotifications;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; }
        finally { managingSamples = false; NotifyEditingState(); }
    }
    private async Task SettleSampleManagerAsync()
    {
        managerCancellation?.Cancel();
        if (activeManager is not null) await activeManager;
        await sampleChangeNotifications;
    }
    private void ReceiveSampleChange(ProjectSamplesChangedMessage change) =>
        sampleChangeNotifications = ApplySampleChangeAsync(sampleChangeNotifications, change);
    private async Task ApplySampleChangeAsync(Task previous, ProjectSamplesChangedMessage change)
    {
        await previous;
        await uiScheduler.InvokeAsync(() =>
        {
            if (deactivated || projectPath != change.Context.ProjectPath || sampleSession != change.Context.SessionId) return;
            if (change.OldFileName is { } oldName && change.NewFileName is { } newName)
            {
                foreach (var track in tracks)
                {
                    if (string.Equals(track.SelectedSampleFileName, oldName, StringComparison.OrdinalIgnoreCase)) track.SelectedSampleFileName = newName;
                    foreach (var node in track.Samples)
                        if (string.Equals(node.FileName, oldName, StringComparison.OrdinalIgnoreCase)) node.FileName = newName;
                }
                history.RenameSample(oldName, newName);
            }
            RefreshAvailableSamples(change.Files, false); CancelNodeDrag(); PruneNodeSelection();
            samplesRevision++; OnPropertyChanged(nameof(SamplesRevision)); NotifyHistoryState(); QueuePlaybackUpdate(); QueueSave();
        }, CancellationToken.None);
    }
}
