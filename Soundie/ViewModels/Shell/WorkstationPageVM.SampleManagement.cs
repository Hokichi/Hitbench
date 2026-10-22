using CommunityToolkit.Mvvm.Input;
using Soundie.Core.Entities;
using Soundie.Resources.DataModels;
using Soundie.Resources.Messages;

namespace Soundie.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private Guid sampleSession = Guid.NewGuid();
    private Task? activeManager;
    private CancellationTokenSource? managerCancellation;
    private Task sampleChangeNotifications = Task.CompletedTask;
    private bool managingSamples;
    public bool CanManageSamples => CanRequestDrawer && sampleFiles is not null && sampleDialog is not null && projectPath is not null && sampleManagementDialog is not null;
    public TrackSampleAvailability GetSampleAvailability(Track track) => track.SelectedSampleFileName is null
        ? TrackSampleAvailability.Unselected : sampleFiles is null || sampleTrackFiles.Any(n => string.Equals(n, track.SelectedSampleFileName, StringComparison.OrdinalIgnoreCase))
            ? TrackSampleAvailability.Available : TrackSampleAvailability.Missing;
    public bool CanEditTrackGrid(Track track) => track is not InstrumentalTrack && tracks.Contains(track) && (sampleFiles is null || GetSampleAvailability(track) == TrackSampleAvailability.Available);
    [RelayCommand(CanExecute = nameof(CanManageSamples))]
    private Task ManageSamplesAsync() => CanManageSamples ? RequestWorkstationDrawerAsync(OHsWorkstationDrawerKind.Samples) : Task.CompletedTask;
    private async Task ManageSamplesCoreAsync(SampleManagementContext context, CancellationToken token, long request)
    {
        try
        {
            await sampleManagementDialog!.ShowAsync(context, token); await sampleChangeNotifications;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; }
        finally { managingSamples = false; if (request == drawerRequestGeneration && requestedDrawerKind == OHsWorkstationDrawerKind.Samples) requestedDrawerKind = null; NotifyEditingState(); }
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
                    if(track is InstrumentalTrack instrument)
                        foreach(var key in instrument.KeyFiles.Keys.ToArray())
                            if(string.Equals(instrument.KeyFiles[key],oldName,StringComparison.OrdinalIgnoreCase)) instrument.KeyFiles[key]=newName;
                    if (string.Equals(track.SelectedSampleFileName, oldName, StringComparison.OrdinalIgnoreCase)) track.SelectedSampleFileName = newName;
                    foreach (var node in track.Samples)
                        if (string.Equals(node.FileName, oldName, StringComparison.OrdinalIgnoreCase)) node.FileName = newName;
                }
                history.RenameSample(oldName, newName);
            }
            RefreshAvailableSamples(change.Files, false, change.IncludesSubfolders); CancelNodeDrag(); PruneNodeSelection();
            samplesRevision++; OnPropertyChanged(nameof(SamplesRevision)); NotifyHistoryState(); QueuePlaybackUpdate(); QueueSave();
            instrumentRevision++; PublishInstrumentSnapshot();
        }, CancellationToken.None);
    }
}
