using CommunityToolkit.Mvvm.Input;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
namespace Hitbench.ViewModels.Shell;
public partial class WorkstationPageVM
{
    private bool trackAppearancePending;
    private CancellationTokenSource? trackAppearanceCancellation;
    private Task? trackAppearanceTask;
    private bool CanChangeTrackColor(Track? track)=>!trackAppearancePending && trackAppearanceDialog is not null && CanEditTracks && track is not null && tracks.Contains(track);
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
        trackAppearanceCancellation=cancellation; trackAppearancePending=true; ChangeTrackColorCommand.NotifyCanExecuteChanged();
        try
        {
            var result=await trackAppearanceDialog!.RequestAsync(new(track.Color,tracks.IndexOf(track)),cancellation.Token);
            if(cancellation.IsCancellationRequested || result is null || !CanEditTracks || deactivated || generation!=loadGeneration || !ReferenceEquals(project,Project) || !tracks.Contains(track))return;
            string? color=null;
            if(result.Color is not null)
            {
                if(!RgbColor.TryParse(result.Color,out var rgb))throw new InvalidOperationException("Invalid track color.");
                color=rgb.ToHex();
            }
            if(track.Color==color)return;
            track.Color = color;
            if (track is InstrumentalTrack) { instrumentRevision++; PublishInstrumentSnapshot(); }
            NotifyTrackPresentation(); QueueSave();
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { }
        catch(Exception error){ if(!deactivated && generation==loadGeneration)ErrorMessage=error.Message; }
        finally{ trackAppearanceCancellation=null; trackAppearancePending=false; ChangeTrackColorCommand.NotifyCanExecuteChanged(); }
    }
}
