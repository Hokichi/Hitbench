using CommunityToolkit.Mvvm.Input;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private bool reloadingSamples;
    private CancellationTokenSource? reloadSamplesCancellation;
    private Task? activeSampleReload;
    private string? sampleReloadError;

    public bool CanReloadSamples => projectPath is not null && Project is not null && sampleFiles is not null &&
        !deactivated && !IsReturning && !reloadingSamples && LoadPhase == Services.ProjectLoadPhase.Ready &&
        !controlPointsBusy && !instrumentBusy && !managingSamples && !folderRenamesActive &&
        !importBusy && !removalBusy && !IsNodeEditBusy && !IsNodeDragActive;

    public bool IsWorkstationContentEnabled => IsInteractionReady;

    [RelayCommand(CanExecute = nameof(CanReloadSamples))]
    private Task ReloadSamplesAsync()
    {
        if (!CanReloadSamples) return Task.CompletedTask;
        reloadSamplesCancellation?.Dispose();
        reloadSamplesCancellation = CancellationTokenSource.CreateLinkedTokenSource(loadCancellation?.Token ?? CancellationToken.None);
        reloadingSamples = true;
        NotifySampleReloadState();
        activeSampleReload = ReloadSamplesCoreAsync(projectPath!, loadGeneration, reloadSamplesCancellation.Token);
        return activeSampleReload;
    }

    private async Task ReloadSamplesCoreAsync(string path, int generation, CancellationToken token)
    {
        var project = Project;
        bool IsCurrent() => !deactivated && !IsReturning && !token.IsCancellationRequested &&
            generation == loadGeneration && projectPath == path && ReferenceEquals(Project, project);
        try
        {
            pendingPlayCancellation?.Cancel();
            await playback.PauseAsync(token);
            RefreshPlayback();
            await sampleChangeNotifications;
            await FlushPlaybackUpdatesAsync();
            token.ThrowIfCancellationRequested();
            if (!IsCurrent()) return;
            var choices = await sampleFiles!.ListAsync(path, token, includeSubfolders: true);
            await CommitAsync(generation, token, () =>
            {
                if (!IsCurrent()) return;
                RefreshAvailableSamples(choices, initializeSelections: false, includesSubfolders: true);
                samplesRevision++; OnPropertyChanged(nameof(SamplesRevision));
                instrumentRevision++; PublishInstrumentSnapshot();
                if (ErrorMessage == sampleReloadError) ErrorMessage = null;
                sampleReloadError = null;
            });
            if (IsCurrent()) await UpdatePlaybackAsync(path, generation, token, forceReload: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            await CommitAsync(generation, CancellationToken.None, () =>
            {
                if (!IsCurrent()) return;
                sampleReloadError = error.Message;
                ErrorMessage = error.Message;
            });
        }
        finally
        {
            reloadingSamples = false;
            NotifySampleReloadState();
        }
    }

    private void NotifySampleReloadAvailability()
    {
        OnPropertyChanged(nameof(CanReloadSamples));
        OnPropertyChanged(nameof(IsWorkstationContentEnabled));
        ReloadSamplesCommand.NotifyCanExecuteChanged();
    }

    private void NotifySampleReloadState()
    {
        OnPropertyChanged(nameof(IsInteractionReady));
        NotifyEditingState();
        NotifyCommandState();
    }
}
