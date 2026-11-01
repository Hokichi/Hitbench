using CommunityToolkit.Mvvm.Input;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
namespace Hitbench.ViewModels.Shell;
public partial class WorkstationPageVM
{
    private bool trackAppearancePending;
    private Track? trackAppearanceTarget;
    internal void CancelTrackAppearance(Track track)
    {
        if (ReferenceEquals(trackAppearanceTarget, track)) trackAppearanceCancellation?.Cancel();
    }
    private CancellationTokenSource? trackAppearanceCancellation;
    private Task? trackAppearanceTask;
    private bool CanChangeTrackColor(Track? track)=>!trackAppearancePending && !IsTrackRenamePending && trackAppearanceDialog is not null && CanEditTracks && track is not null && tracks.Contains(track);
    [RelayCommand(CanExecute=nameof(CanChangeTrackColor))]
    private Task ChangeTrackColorAsync(Track track)
    {
        if(!CanChangeTrackColor(track))return Task.CompletedTask;
        trackAppearanceTask=ChangeTrackAppearanceCoreAsync(track); return trackAppearanceTask;
    }
    private async Task ChangeTrackAppearanceCoreAsync(Track track)
    {
        var project=Project; var generation=loadGeneration;
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(loadCancellation?.Token??CancellationToken.None);
        trackAppearanceTarget=track; trackAppearanceCancellation=cancellation; trackAppearancePending=true; ChangeTrackColorCommand.NotifyCanExecuteChanged();
        try
        {
            var result=await trackAppearanceDialog!.RequestAsync(new(track.Color,tracks.IndexOf(track)) { Name = track.Name },cancellation.Token);
            if(cancellation.IsCancellationRequested || result is null || !CanEditTracks || deactivated || generation!=loadGeneration || !ReferenceEquals(project,Project) || !tracks.Contains(track))return;
            string? color=null;
            if(result.Color is not null)
            {
                if(!RgbColor.TryParse(result.Color,out var rgb))throw new InvalidOperationException("Invalid track color.");
                color=rgb.ToHex();
            }
            var name = result.Name ?? track.Name;
            if(track.Color==color && track.Name==name)return;
            if (track.Name != name && track is InstrumentalTrack { FolderName.Length: > 0 } instrument && sampleFiles is not null)
            {
                await SettleInstrumentalEditorAsync();
                await SettleSampleManagerAsync();
                await SettleInstrumentalFoldersAsync();
                await DrainProjectSaveQueueAsync();
                if (cancellation.IsCancellationRequested || deactivated || generation != loadGeneration || !ReferenceEquals(project, Project) || !tracks.Contains(track)) return;
                await RenameInstrumentFolderAsync(instrument, name, new(color) { Name = name }, () => cancellation.IsCancellationRequested);
                return;
            }
            track.Name = name;
            track.Color = color;
            if (track is InstrumentalTrack) { instrumentRevision++; PublishInstrumentSnapshot(); }
            NotifyTrackPresentation(); QueueSave();
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { }
        catch(Exception error){ if(!deactivated && generation==loadGeneration)ErrorMessage=error.Message; }
        finally{ trackAppearanceTarget=null; trackAppearanceCancellation=null; trackAppearancePending=false; ChangeTrackColorCommand.NotifyCanExecuteChanged(); }
    }
}
