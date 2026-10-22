using System.IO;
using CommunityToolkit.Mvvm.Input;
namespace Soundie.ViewModels.Shell;
public partial class WorkstationPageVM
{
    private CancellationTokenSource? folderOpenCancellation;
    private Task folderOpen = Task.CompletedTask;
    private bool CanOpenProjectFolder() => directoryLauncher is not null && IsInteractionReady && !deactivated && !IsReturning && projectPath is not null && folderOpenCancellation is null;
    private bool CanOpenBeatmapFolder() => CanOpenProjectFolder() && !string.IsNullOrWhiteSpace(Project?.BeatmapPath);
    private void NotifyFolderCommands() { OpenProjectFolderCommand.NotifyCanExecuteChanged(); OpenBeatmapFolderCommand.NotifyCanExecuteChanged(); }
    [RelayCommand(CanExecute = nameof(CanOpenProjectFolder))]
    private Task OpenProjectFolderAsync(CancellationToken token) => CanOpenProjectFolder() ? StartFolderOpenAsync(false, token) : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanOpenBeatmapFolder))]
    private Task OpenBeatmapFolderAsync(CancellationToken token) => CanOpenBeatmapFolder() ? StartFolderOpenAsync(true, token) : Task.CompletedTask;
    private Task StartFolderOpenAsync(bool beatmap, CancellationToken token)
    {
        folderOpenCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        NotifyFolderCommands();
        folderOpen = OpenFolderAsync(beatmap, folderOpenCancellation.Token);
        return folderOpen;
    }
    private async Task OpenFolderAsync(bool beatmap, CancellationToken token)
    {
        var generation = loadGeneration;
        try
        {
            var directory = beatmap ? Path.GetFullPath(Project!.BeatmapPath) : Path.GetDirectoryName(Path.GetFullPath(projectPath!))!;
            await directoryLauncher!.OpenAsync(directory, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { if (!token.IsCancellationRequested && !deactivated && !IsReturning && generation == loadGeneration) ErrorMessage = error.Message; }
        finally { folderOpenCancellation?.Dispose(); folderOpenCancellation = null; NotifyFolderCommands(); }
    }
    private async Task SettleFolderOpenAsync() { folderOpenCancellation?.Cancel(); await folderOpen; }
}
