using CommunityToolkit.Mvvm.Input;
using Soundie.Core.Entities;
using Soundie.Resources.DataModels;
namespace Soundie.ViewModels.Shell;
public partial class WorkstationPageVM
{
    private bool trackAppearancePending;
    private CancellationTokenSource? trackAppearanceCancellation;
    private Task? trackAppearanceTask;
    private bool CanChangeTrackNameAndColor(Track? track)=>!trackAppearancePending && trackAppearanceDialog is not null && CanEditTracks && track is not null and not InstrumentalTrack && tracks.Contains(track);
    [RelayCommand(CanExecute=nameof(CanChangeTrackNameAndColor))]
    private Task ChangeTrackNameAndColorAsync(Track track)
    {
        if(!CanChangeTrackNameAndColor(track))return Task.CompletedTask;
        trackAppearanceTask=ChangeTrackAppearanceCoreAsync(track); return trackAppearanceTask;
    }
    private async Task ChangeTrackAppearanceCoreAsync(Track track)
    {
        var project=Project; var generation=loadGeneration;
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(loadCancellation?.Token??CancellationToken.None);
        trackAppearanceCancellation=cancellation; trackAppearancePending=true; ChangeTrackNameAndColorCommand.NotifyCanExecuteChanged();
        try
        {
            var result=await trackAppearanceDialog!.RequestAsync(new(track.Name,track.Color,tracks.IndexOf(track)),cancellation.Token);
            if(cancellation.IsCancellationRequested || result is null || !CanEditTracks || deactivated || generation!=loadGeneration || !ReferenceEquals(project,Project) || !tracks.Contains(track))return;
            string? color=null;
            if(result.Color is not null)
            {
                if(!RgbColor.TryParse(result.Color,out var rgb))throw new InvalidOperationException("Invalid track color.");
                color=rgb.ToHex();
            }
            if(track.Name==result.Name && track.Color==color)return;
            track.Name=result.Name; track.Color=color; NotifyTrackPresentation(); QueueSave();
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { }
        catch(Exception error){ if(!deactivated && generation==loadGeneration)ErrorMessage=error.Message; }
        finally{ trackAppearanceCancellation=null; trackAppearancePending=false; ChangeTrackNameAndColorCommand.NotifyCanExecuteChanged(); }
    }
}
