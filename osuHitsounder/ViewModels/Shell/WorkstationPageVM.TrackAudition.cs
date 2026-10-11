using osuHitsounder.Core.Entities;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private int trackAuditionRevision;
    public int TrackAuditionRevision => trackAuditionRevision;
    public bool IsTrackMuted(Track track) => track.IsTrackMuted;
    public bool IsTrackSoloed(Track track) => track.IsTrackSoloed;

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
        trackAuditionRevision++;
        OnPropertyChanged(nameof(TrackAuditionRevision));
        QueuePlaybackUpdate();
        QueueSave();
    }
}
