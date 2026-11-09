using Hitbench.Services;

namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private readonly PlaybackPresentationClock presentationClock = new();
    private long observedPresentationRevision = -1;
    private TimeSpan presentationPosition;
    public TimeSpan PresentationPosition => presentationPosition;
    public TimeSpan DisplayPosition => IsStaticMarker ? PresentationPosition : Position;

    public void AdvancePlaybackPresentation(TimeSpan frameTime)
    {
        var snapshot = playback.Presentation;
        observedPresentationRevision = snapshot.Revision;
        SetPresentationPosition(presentationClock.GetPosition(snapshot, frameTime, Waveform?.Duration ?? TimeSpan.Zero));
    }

    public void ResetPlaybackPresentation()
    {
        presentationClock.Reset();
        var snapshot = playback.Presentation;
        observedPresentationRevision = snapshot.Revision;
        SetPresentationPosition(snapshot.Playback.Position);
    }

    private void SetPresentationPosition(TimeSpan value)
    {
        if (SetProperty(ref presentationPosition, value, nameof(PresentationPosition)) && IsStaticMarker)
            OnPropertyChanged(nameof(DisplayPosition));
    }
}
