using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;

namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private Task skinDefaultsOperation = Task.CompletedTask;
    private CancellationTokenSource? skinDefaultsCancellation;
    private IReadOnlyList<string> skinDefaultFiles = Array.Empty<string>();
    public IReadOnlyList<string> SkinDefaultFiles => skinDefaultFiles;
    public bool AllowDefaults => Project?.AllowDefaults == true;
    public bool IsRefreshingSkinDefaults { get; private set; }

    private void RegisterSkinDefaultsMessages()
    {
        messenger?.Register<OHsSkinDefaultsStateRequestedMessage>(this, (_, m) => { if (OwnsSkinContext(m.Context)) PublishSkinDefaultsState(); });
        messenger?.Register<OHsSetAllowDefaultsRequestedMessage>(this, (_, m) => { if (OwnsSkinContext(m.Context)) SetAllowDefaultsAsync(m.AllowDefaults); });
    }
    private bool OwnsSkinContext(SampleManagementContext context) => !deactivated && context.ProjectPath == projectPath && context.SessionId == sampleSession;
    private void PublishSkinDefaultsState()
    {
        if (projectPath is not null && Project is { } project)
            messenger?.Send(new OHsSkinDefaultsStateChangedMessage(new(projectPath, project.BeatmapPath, sampleSession), project.AllowDefaults, project.CurrentSkin, skinDefaultFiles, IsRefreshingSkinDefaults));
    }
    public Task SetAllowDefaultsAsync(bool value, CancellationToken token = default)
    {
        if (IsRefreshingSkinDefaults || !CanEditTracks || Project is null || projectPath is null || value == AllowDefaults) return skinDefaultsOperation;
        skinDefaultsCancellation?.Dispose();
        skinDefaultsCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, loadCancellation?.Token ?? default);
        skinDefaultsOperation = ChangeAllowDefaultsAsync(value, projectPath, Project, loadGeneration, skinDefaultsCancellation.Token);
        return skinDefaultsOperation;
    }
    private async Task ChangeAllowDefaultsAsync(bool value, string path, ProjectData project, int generation, CancellationToken token)
    {
        IsRefreshingSkinDefaults = true; NotifySkinDefaultsState();
        try
        {
            await playback.PauseAsync(token);
            async Task Refresh(CancellationToken cancellation)
            {
                var snapshot = skinDefaults is null ? new OHsSkinDefaultsSnapshot(project.CurrentSkin, skinDefaultFiles, false)
                    : await skinDefaults.RefreshAsync(path, project.CurrentSkin, value, value, cancellation);
                await CommitAsync(generation, cancellation, () =>
                {
                    if (!IsCurrentProject(path, project, generation)) return;
                    project.AllowDefaults = value; project.CurrentSkin = snapshot.SkinName; skinDefaultFiles = snapshot.Files;
                    QueueSave();
                });
                if (IsCurrentProject(path, project, generation)) await UpdatePlaybackAsync(path, generation, cancellation, forceReload: snapshot.WasReplaced);
            }
            if (value && audioOperationProgress is not null) await audioOperationProgress.RunAsync("Loading default samples", Refresh, token, showImmediately: true);
            else await Refresh(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { await CommitAsync(generation, default, () => ErrorMessage = error.Message); }
        finally
        {
            await CommitAsync(generation, default, () => { IsRefreshingSkinDefaults = false; NotifySkinDefaultsState(); });
        }
    }
    private void NotifySkinDefaultsState()
    {
        OnPropertyChanged(nameof(AllowDefaults)); OnPropertyChanged(nameof(SkinDefaultFiles)); OnPropertyChanged(nameof(IsRefreshingSkinDefaults));
        OnPropertyChanged(nameof(IsInteractionReady)); NotifyEditingState(); NotifyCommandState(); PublishSkinDefaultsState();
    }
    private async Task SettleSkinDefaultsAsync()
    {
        skinDefaultsCancellation?.Cancel(); await skinDefaultsOperation;
        IsRefreshingSkinDefaults = false;
    }
}
