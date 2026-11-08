using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Core.Entities;
using Hitbench.Resources.Messages;
using Hitbench.Services.FileSystem;
namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private CancellationTokenSource? mapsetImportCancellation;
    private bool HasMapsetFolderConflict => tracks.OfType<InstrumentalTrack>().Any(t =>
        string.Equals(t.FolderName, OHsMapsetSamples.FolderName, StringComparison.OrdinalIgnoreCase));
    public bool CanImportFromMapset => mapsetImports is not null && sampleFiles is not null && projectPath is not null &&
        !string.IsNullOrWhiteSpace(Project?.BeatmapPath) && !HasMapsetFolderConflict && CanEditTracks &&
        !folderRenamesActive && !instrumentBusy && !IsNodeEditBusy && !IsNodeDragActive && !importBusy && !removalBusy && !settlingDrawers;

    private async Task CancelMapsetImportAsync()
    {
        mapsetImportCancellation?.Cancel();
        if (mapsetImportCancellation is not null && activeImport is not null) await activeImport;
    }

    [RelayCommand(CanExecute = nameof(CanImportFromMapset))]
    private Task ImportFromMapsetAsync()
    {
        if (!CanImportFromMapset) return Task.CompletedTask;
        var context = SampleManagementContext!; var project = Project; var generation = loadGeneration;
        mapsetImportCancellation = CancellationTokenSource.CreateLinkedTokenSource(loadCancellation?.Token ?? default);
        var cancellation = mapsetImportCancellation; var token = cancellation.Token;
        importBusy = true; NotifyEditingState();
        activeImport = RunAsync(); return activeImport;

        async Task<IReadOnlyList<string>> RunAsync()
        {
            var reserved = false;
            try
            {
                if (managingSamples)
                {
                    if (messenger is null) return [];
                    var request = messenger.Send(new OHsPrepareSampleDropImportMessage(context));
                    if (!request.HasReceivedResponse || !await request.Response) return [];
                    reserved = true;
                }
                token.ThrowIfCancellationRequested();
                pendingPlayCancellation?.Cancel(); await playback.PauseAsync(token); RefreshPlayback();
                var result = await mapsetImports!.ImportAsync(context.ProjectPath, project!.BeatmapPath, token);
                if (!IsCurrentProject(context.ProjectPath, project, generation) || token.IsCancellationRequested) return result.ImportedFiles;
                if (result.ImportedFiles.Count == 0)
                {
                    if (result.Failures.Count > 0) ErrorMessage = string.Join(Environment.NewLine, result.Failures);
                    return [];
                }
                var choices = await sampleFiles!.ListAsync(context.ProjectPath, token, includeSubfolders: true);
                await uiScheduler.InvokeAsync(() =>
                {
                    if (!IsCurrentProject(context.ProjectPath, project, generation) || token.IsCancellationRequested) return;
                    if (messenger is not null) messenger.Send(new ProjectSamplesChangedMessage(context, choices, reloadSamples: true));
                    else { RefreshAvailableSamples(choices, false, true); QueuePlaybackUpdate(forceReload: true); QueueSave(); }
                    ErrorMessage = result.Failures.Count > 0 ? string.Join(Environment.NewLine, result.Failures) : null;
                }, token);
                await sampleChangeNotifications; await FlushPlaybackUpdatesAsync(); return result.ImportedFiles;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return []; }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested && IsCurrentProject(context.ProjectPath, project, generation)) ErrorMessage = error.Message;
                return [];
            }
            finally
            {
                try
                {
                    if (reserved && messenger is not null)
                        await uiScheduler.InvokeAsync(() => messenger.Send(new OHsSampleDropImportFinishedMessage(context)), CancellationToken.None);
                }
                finally
                {
                    if (ReferenceEquals(mapsetImportCancellation, cancellation)) mapsetImportCancellation = null;
                    cancellation.Dispose(); importBusy = false; NotifyEditingState();
                }
            }
        }
    }
}
