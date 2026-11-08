using CommunityToolkit.Mvvm.Input;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;

namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private Guid sampleSession = Guid.NewGuid();
    private Task? activeManager;
    private CancellationTokenSource? managerCancellation;
    private Task sampleChangeNotifications = Task.CompletedTask;
    private bool managingSamples;
    private bool samplesRequestedOpen;
    private long sampleManagerRequestGeneration;
    private Task sampleManagerTransitions = Task.CompletedTask;
    private Task<bool>? sampleManagerClosing;
    public bool IsSamplesOpen => managingSamples;
    public bool CanManageSamples => CanRequestDrawer && sampleFiles is not null && sampleDialog is not null && projectPath is not null && sampleManagementDialog is not null;
    public TrackSampleAvailability GetSampleAvailability(Track track) => track.UsesDefaultSample ? IsDefaultSampleAvailable(track) ? TrackSampleAvailability.Available : TrackSampleAvailability.Missing : track.SelectedSampleFileName is null
        ? TrackSampleAvailability.Unselected : sampleFiles is null || sampleTrackFiles.Any(n => string.Equals(n, track.SelectedSampleFileName, StringComparison.OrdinalIgnoreCase))
            ? TrackSampleAvailability.Available : TrackSampleAvailability.Missing;
    public bool CanEditTrackGrid(Track track) => track is not InstrumentalTrack && tracks.Contains(track) && (sampleFiles is null || GetSampleAvailability(track) == TrackSampleAvailability.Available);
    [RelayCommand(CanExecute = nameof(CanManageSamples))]
    private Task ManageSamplesAsync()
    {
        if (!CanManageSamples) return Task.CompletedTask;
        samplesRequestedOpen = !samplesRequestedOpen;
        var request = ++sampleManagerRequestGeneration;
        sampleManagerTransitions = TransitionSampleManagerAsync(sampleManagerTransitions, request, sampleSession);
        return Task.CompletedTask;
    }

    private async Task TransitionSampleManagerAsync(Task previous, long request, Guid session)
    {
        try
        {
            await previous;
            if (request != sampleManagerRequestGeneration || session != sampleSession || !CanRequestDrawer) return;
            if (!samplesRequestedOpen)
            {
                if (!await CloseSampleManagerAsync(loadCancellation?.Token ?? default) && request == sampleManagerRequestGeneration)
                    samplesRequestedOpen = managingSamples;
                return;
            }
            if (sampleManagerClosing is { } closing) await closing;
            if (request != sampleManagerRequestGeneration || session != sampleSession || !CanRequestDrawer) return;
            if (managingSamples) return;
            if (mappingTransportOwner is not null) await SettleInstrumentalEditorAsync();
            await DrainProjectSaveQueueAsync();
            if (request != sampleManagerRequestGeneration || session != sampleSession || !CanRequestDrawer) return;
            await SettleInstrumentalFoldersAsync();
            if (request != sampleManagerRequestGeneration || session != sampleSession || !CanRequestDrawer || projectPath is null) return;
            if (activeManager is not null) await activeManager;
            managerCancellation?.Dispose();
            sampleManagerClosing = null;
            managerCancellation = CancellationTokenSource.CreateLinkedTokenSource(loadCancellation?.Token ?? default);
            managingSamples = true;
            OnPropertyChanged(nameof(IsSamplesOpen)); NotifyEditingState();
            activeManager = ManageSamplesCoreAsync(new(projectPath, Project?.BeatmapPath, session, HasMapsetFolderConflict), managerCancellation.Token, request);
        }
        catch (OperationCanceledException) when (settlingDrawers || loadCancellation?.IsCancellationRequested == true || managerCancellation?.IsCancellationRequested == true) { }
        catch (Exception error) { ErrorMessage = error.Message; if (request == sampleManagerRequestGeneration) samplesRequestedOpen = managingSamples; }
    }
    private async Task ManageSamplesCoreAsync(SampleManagementContext context, CancellationToken token, long request)
    {
        try
        {
            await sampleManagementDialog!.ShowAsync(context, token); await sampleChangeNotifications;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; }
        finally
        {
            managingSamples = false;
            if (request == sampleManagerRequestGeneration) samplesRequestedOpen = false;
            OnPropertyChanged(nameof(IsSamplesOpen)); NotifyEditingState();
        }
    }
    private Task<bool> CloseSampleManagerAsync(CancellationToken token) =>
        sampleManagerClosing is { IsCompleted: false } closing ? closing : sampleManagerClosing = CloseSampleManagerCoreAsync(token);

    private async Task<bool> CloseSampleManagerCoreAsync(CancellationToken token)
    {
        if (activeManager is not { } manager) return true;
        using var closing = CancellationTokenSource.CreateLinkedTokenSource(token, managerCancellation?.Token ?? default);
        if (!manager.IsCompleted && !await sampleManagementDialog!.TryCloseAsync(SampleManagementContext!, closing.Token)) return false;
        await manager;
        await sampleChangeNotifications;
        return true;
    }
    private async Task SettleSampleManagerAsync()
    {
        ++sampleManagerRequestGeneration;
        samplesRequestedOpen = false;
        managerCancellation?.Cancel();
        await sampleManagerTransitions;
        if (sampleManagerClosing is { } closing)
        { try { await closing; } catch (OperationCanceledException) when (managerCancellation?.IsCancellationRequested == true) { } }
        if (activeManager is not null) await activeManager;
        await sampleChangeNotifications;
        activeManager = null;
        managerCancellation?.Dispose(); managerCancellation = null;
        sampleManagerClosing = null;
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
            samplesRevision++; OnPropertyChanged(nameof(SamplesRevision)); NotifyHistoryState(); QueuePlaybackUpdate(change.ReloadSamples); QueueSave();
            instrumentRevision++; PublishInstrumentSnapshot();
        }, CancellationToken.None);
    }
}
