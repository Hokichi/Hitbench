using CommunityToolkit.Mvvm.Input;
using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;

namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private bool hitNormalBusy;
    private Task hitNormalOperation = Task.CompletedTask;
    private CancellationTokenSource? hitNormalCancellation;
    public bool CanConfigHitNormal => hitNormalDialog is not null && projectPath is not null && CanEditTracks &&
        !hitNormalBusy && !folderRenamesActive && !instrumentBusy && !managingSamples && !importBusy && !removalBusy && !IsNodeEditBusy;

    [RelayCommand(CanExecute = nameof(CanConfigHitNormal))]
    private Task ConfigHitNormalAsync()
    {
        if (!CanConfigHitNormal) return Task.CompletedTask;
        hitNormalCancellation = CancellationTokenSource.CreateLinkedTokenSource(loadCancellation?.Token ?? default);
        hitNormalBusy = true; NotifyHitNormalState();
        hitNormalOperation = ConfigureHitNormalCoreAsync(hitNormalCancellation.Token);
        return hitNormalOperation;
    }

    private async Task ConfigureHitNormalCoreAsync(CancellationToken token)
    {
        var project = Project!; var path = projectPath!; var generation = loadGeneration;
        try
        {
            var current = OHsHitNormalConfiguration.FromProject(project);
            var result = await hitNormalDialog!.RequestAsync(path, project.BeatmapPath, current, token);
            if (token.IsCancellationRequested || result is null || IsReturning || !IsCurrentProject(path, project, generation)) return;
            if (new[] { result.NormalPath, result.SoftPath, result.DrumPath }.Any(p => OHsHitNormalSamples.GetPathError(p) is not null))
                throw new InvalidOperationException("Invalid hitnormal sample path.");
            if (result == current) return;
            result.ApplyTo(project); QueueSave();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { if (!token.IsCancellationRequested && IsCurrentProject(path, project, generation)) ErrorMessage = error.Message; }
        finally
        {
            hitNormalCancellation?.Dispose(); hitNormalCancellation = null; hitNormalBusy = false; NotifyHitNormalState();
        }
    }

    private void NotifyHitNormalState()
    {
        OnPropertyChanged(nameof(IsInteractionReady)); NotifyEditingState(); NotifyCommandState();
    }

    private void NotifyHitNormalCommand() { OnPropertyChanged(nameof(CanConfigHitNormal)); ConfigHitNormalCommand.NotifyCanExecuteChanged(); }
    private async Task SettleHitNormalAsync() { hitNormalCancellation?.Cancel(); await hitNormalOperation; }
}
