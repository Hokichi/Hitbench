namespace Onset.Resources.DataModels;

public sealed class InstrumentalEditorContext
{
    public InstrumentalTrackSnapshot Track { get; }
    public OHsInstrumentalTimelineSnapshot Timeline { get; }
    public InstrumentalEditorContext(InstrumentalTrackSnapshot track, OHsInstrumentalTimelineSnapshot timeline)
    {
        if (track.Context != timeline.Context) throw new ArgumentException("Track and timeline must belong to the same editor.", nameof(timeline));
        Track = track; Timeline = timeline;
    }
}
