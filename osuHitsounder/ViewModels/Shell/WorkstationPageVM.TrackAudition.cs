using osuHitsounder.Core.Entities;
using CommunityToolkit.Mvvm.Input;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private int trackAuditionRevision;
    public int TrackAuditionRevision => trackAuditionRevision;
    public bool IsTrackMuted(Track track) => track.IsTrackMuted;
    public bool IsTrackSoloed(Track track) => track.IsTrackSoloed;

    public bool HasMutedTracks => tracks.Any(track => track.IsTrackMuted);
    public bool HasSoloedTracks => tracks.Any(track => track.IsTrackSoloed);
    public bool CanClearTrackMutes => CanPlaceSamples && HasMutedTracks;
    public bool CanClearTrackSolos => CanPlaceSamples && HasSoloedTracks;

    [RelayCommand(CanExecute = nameof(CanClearTrackMutes))]
    private void ClearTrackMutes()
    {
        if (!CanClearTrackMutes) return;
        foreach (var track in tracks) track.IsTrackMuted = false;
        NotifyTrackAuditionChanged();
    }

    [RelayCommand(CanExecute = nameof(CanClearTrackSolos))]
    private void ClearTrackSolos()
    {
        if (!CanClearTrackSolos) return;
        foreach (var track in tracks) track.IsTrackSoloed = false;
        NotifyTrackAuditionChanged();
    }

    private void NotifyTrackAuditionState()
    {
        OnPropertyChanged(nameof(HasMutedTracks)); OnPropertyChanged(nameof(HasSoloedTracks));
        OnPropertyChanged(nameof(CanClearTrackMutes)); OnPropertyChanged(nameof(CanClearTrackSolos));
        ClearTrackMutesCommand.NotifyCanExecuteChanged(); ClearTrackSolosCommand.NotifyCanExecuteChanged();
    }

    public void SetTrackMuted(Track track, bool value)
    {
        if (!CanPlaceSamples || !tracks.Contains(track) || track.IsTrackMuted == value) return;
        track.IsTrackMuted = value;
        NotifyTrackAuditionChanged();
    }

    public void SetTrackSoloed(Track track, bool value)
    {
        if (!CanPlaceSamples || !tracks.Contains(track) || track.IsTrackSoloed == value) return;
        track.IsTrackSoloed = value;
        NotifyTrackAuditionChanged();
    }

    private void NotifyTrackAuditionChanged()
    {
        NotifyTrackAuditionState();
        trackAuditionRevision++;
        OnPropertyChanged(nameof(TrackAuditionRevision));
        QueuePlaybackUpdate();
        QueueSave();
    }
}
