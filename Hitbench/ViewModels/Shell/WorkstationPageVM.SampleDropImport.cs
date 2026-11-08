using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.Services.FileSystem;
using CommunityToolkit.Mvvm.Messaging;
namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private CancellationTokenSource? droppedImportCancellation;
    private async Task CancelDroppedImportAsync() { droppedImportCancellation?.Cancel(); if (droppedImportCancellation is not null && activeImport is not null) await activeImport; }
    public SampleManagementContext? SampleManagementContext => projectPath is null ? null : new(projectPath, Project?.BeatmapPath, sampleSession, HasMapsetFolderConflict);
    public bool CanImportDroppedSamples => sampleDropDialog is not null && sampleFiles is not null && projectPath is not null && CanEditTracks && !folderRenamesActive && !instrumentBusy && !IsNodeEditBusy && !importBusy && !removalBusy && !settlingDrawers;
    public Task<IReadOnlyList<string>> ImportDroppedSamplesAsync(IReadOnlyList<string> paths)
    {
        if (!CanImportDroppedSamples) return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        importBusy = true; NotifyEditingState(); var context = SampleManagementContext!; var generation = loadGeneration; var project = Project; droppedImportCancellation?.Dispose(); droppedImportCancellation = CancellationTokenSource.CreateLinkedTokenSource(loadCancellation?.Token ?? default); var token = droppedImportCancellation.Token;
        activeImport = RunAsync(); return activeImport;
        async Task<IReadOnlyList<string>> RunAsync()
        {
            var reserved = false;
            try
            {
                var eligible = await OHsSampleDropFiles.GetEligibleAsync(paths, token);
                if (eligible.Count == 0 || !IsCurrentProject(context.ProjectPath, project, generation)) return Array.Empty<string>();
                if (managingSamples)
                {
                    if (messenger is null) return Array.Empty<string>();
                    var request = messenger.Send(new OHsPrepareSampleDropImportMessage(context));
                    if (!request.HasReceivedResponse || !await request.Response) return Array.Empty<string>(); reserved = true;
                }
                var selected = await sampleDropDialog!.SelectAsync(eligible, token);
                token.ThrowIfCancellationRequested(); if (!IsCurrentProject(context.ProjectPath, project, generation)) return Array.Empty<string>();
                var sources = selected.Where(p => eligible.Contains(p, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (sources.Length == 0) return Array.Empty<string>();
                return audioOperationProgress is null ? await CopyAsync(sources) : await audioOperationProgress.RunAsync("Importing samples...", _ => CopyAsync(sources), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return Array.Empty<string>(); }
            catch (Exception e) { if (IsCurrentProject(context.ProjectPath, project, generation)) ErrorMessage = e.Message; return Array.Empty<string>(); }
            finally
            {
                try
                {
                    if (reserved && messenger is not null) await uiScheduler.InvokeAsync(() => messenger.Send(new OHsSampleDropImportFinishedMessage(context)), CancellationToken.None);
                }
                finally
                {
                    droppedImportCancellation?.Dispose(); droppedImportCancellation = null; importBusy = false; NotifyEditingState();
                }
            }
        }
        async Task<IReadOnlyList<string>> CopyAsync(IReadOnlyList<string> sources)
        {
            pendingPlayCancellation?.Cancel(); await playback.PauseAsync(token); RefreshPlayback();
            var result = await (sampleImports ?? new(sampleFiles!)).ImportAsync(context.ProjectPath, sources, token);
            if (!IsCurrentProject(context.ProjectPath, project, generation)) return result.ImportedFiles;
            var choices = await sampleFiles!.ListAsync(context.ProjectPath, CancellationToken.None, includeSubfolders: true);
            await uiScheduler.InvokeAsync(() =>
            {
                if (!IsCurrentProject(context.ProjectPath, project, generation)) return;
                if (messenger is not null) messenger.Send(new ProjectSamplesChangedMessage(context, choices));
                else { RefreshAvailableSamples(choices, false, true); QueuePlaybackUpdate(); QueueSave(); }
                ErrorMessage = result.Failures.Count > 0 ? string.Join(Environment.NewLine, result.Failures) : null;
            }, CancellationToken.None);
            await sampleChangeNotifications; await FlushPlaybackUpdatesAsync(); return result.ImportedFiles;
        }
    }
}
