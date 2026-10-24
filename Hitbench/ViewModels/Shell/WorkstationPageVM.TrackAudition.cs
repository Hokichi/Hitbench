using Hitbench.Core.Entities;
using CommunityToolkit.Mvvm.Input;

namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private int trackAuditionRevision;
    public int TrackAuditionRevision => trackAuditionRevision;
    public bool IsTrackMuted(Track track) => track.IsTrackMuted;
    public bool IsTrackSoloed(Track track) => track.IsTrackSoloed;

    public bool AreTracksMuted => tracks.Count > 0 && tracks.All(t => t.IsTrackMuted);
    public bool AreTracksSoloed => tracks.Count > 0 && tracks.All(t => t.IsTrackSoloed);
    public bool CanToggleTrackMutes => CanPlaceSamples && tracks.Count > 0;
    public string TrackMutesActionText => AreTracksMuted ? "Unmute all tracks" : "Mute all tracks";
    [RelayCommand(CanExecute = nameof(CanToggleTrackMutes))]
    private void ToggleTrackMutes()
    {
        if (!CanToggleTrackMutes) return;
        var value = !AreTracksMuted;
        foreach (var track in tracks) track.IsTrackMuted = value;
        NotifyTrackAuditionChanged();
    }
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
        foreach (var name in new[] { nameof(AreTracksMuted), nameof(AreTracksSoloed), nameof(CanToggleTrackMutes), nameof(TrackMutesActionText) }) OnPropertyChanged(name);
        ToggleTrackMutesCommand.NotifyCanExecuteChanged();
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
