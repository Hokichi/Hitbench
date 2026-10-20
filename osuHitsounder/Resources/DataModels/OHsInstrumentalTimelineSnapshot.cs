namespace osuHitsounder.Resources.DataModels;

public sealed class OHsInstrumentalTimelineSnapshot(InstrumentalTrackContext context, long revision,
    TimeSpan duration, TimeSpan position, bool isPlaying, bool canEdit, bool canUseTransport,
    IReadOnlyList<int> offsets, IReadOnlyList<int> bpms, double zoomFactor,
    int selectedSubdivisionIndex, bool scrollWithMarker, string? playbackWarning, bool isAdditionMode = true)
{
    public InstrumentalTrackContext Context { get; } = context;
    public long Revision { get; } = revision;
    public TimeSpan Duration { get; } = duration;
    public TimeSpan Position { get; } = position;
    public bool IsAdditionMode { get; } = isAdditionMode;
    public bool IsPlaying { get; } = isPlaying;
    public bool CanEdit { get; } = canEdit;
    public bool CanUseTransport { get; } = canUseTransport;
    public IReadOnlyList<int> Offsets { get; } = Array.AsReadOnly(offsets.ToArray());
    public IReadOnlyList<int> Bpms { get; } = Array.AsReadOnly(bpms.ToArray());
    public double ZoomFactor { get; } = zoomFactor;
    public int SelectedSubdivisionIndex { get; } = selectedSubdivisionIndex is >= 0 and <= 8
        ? selectedSubdivisionIndex : throw new ArgumentOutOfRangeException(nameof(selectedSubdivisionIndex));
    public int Subdivision => new[] { 1, 2, 3, 4, 5, 6, 8, 12, 16 }[SelectedSubdivisionIndex];
    public bool ScrollWithMarker { get; } = scrollWithMarker;
    public string? PlaybackWarning { get; } = playbackWarning;
}
