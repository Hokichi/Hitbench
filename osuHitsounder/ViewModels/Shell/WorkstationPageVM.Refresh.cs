using CommunityToolkit.Mvvm.Input;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private bool refreshingProject;
    private CancellationTokenSource? refreshCancellation;
    private Task? activeRefresh;

    public bool CanRefreshProject => projectPath is not null && !deactivated && !IsReturning && !refreshingProject &&
        LoadPhase is Services.ProjectLoadPhase.Ready or Services.ProjectLoadPhase.Failed &&
        !controlPointsBusy && !instrumentBusy && !managingSamples && !folderRenamesActive &&
        !importBusy && !removalBusy && !IsNodeEditBusy && !IsNodeDragActive;

    public bool IsWorkstationContentEnabled => IsInteractionReady ||
        LoadPhase == Services.ProjectLoadPhase.Failed && CanRefreshProject;

    [RelayCommand(CanExecute = nameof(CanRefreshProject))]
    private Task RefreshProjectAsync()
    {
        if (!CanRefreshProject) return Task.CompletedTask;
        refreshCancellation?.Dispose();
        refreshCancellation = new CancellationTokenSource();
        refreshingProject = true;
        NotifyRefreshState();
        activeRefresh = RefreshProjectCoreAsync(projectPath!, loadGeneration, refreshCancellation.Token);
        return activeRefresh;
    }

    private async Task RefreshProjectCoreAsync(string path, int generation, CancellationToken token)
    {
        try
        {
            pendingPlayCancellation?.Cancel();
            await playback.PauseAsync(token);
            RefreshPlayback();
            await FlushSavesAsync();
            await FlushPlaybackUpdatesAsync();
            token.ThrowIfCancellationRequested();
            if (deactivated || IsReturning || generation != loadGeneration || projectPath != path) return;
            await LoadAsync(path, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!deactivated && projectPath == path && generation == loadGeneration) ErrorMessage = error.Message;
        }
        finally
        {
            refreshingProject = false;
            NotifyRefreshState();
        }
    }

    private void NotifyRefreshAvailability()
    {
        OnPropertyChanged(nameof(CanRefreshProject));
        OnPropertyChanged(nameof(IsWorkstationContentEnabled));
        RefreshProjectCommand.NotifyCanExecuteChanged();
    }

    private void NotifyRefreshState()
    {
        OnPropertyChanged(nameof(IsInteractionReady));
        NotifyEditingState();
        NotifyCommandState();
    }
}
