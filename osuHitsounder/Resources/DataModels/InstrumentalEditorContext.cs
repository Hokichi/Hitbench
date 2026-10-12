using osuHitsounder.Core.Entities;
namespace osuHitsounder.Resources.DataModels;

public sealed class InstrumentalEditorContext(InstrumentalTrackSnapshot track, string audioPath, WaveformData waveform, IReadOnlyList<int> offsets, IReadOnlyList<int> bpms, TimeSpan initialPosition, int initialSubdivision, double initialZoomFactor, int songVolume, int samplesVolume, int applicationVolume)
{
    public InstrumentalTrackSnapshot Track { get; } = track;
    public string AudioPath { get; } = audioPath;
    public WaveformData Waveform { get; } = waveform;
    public IReadOnlyList<int> Offsets { get; } = Array.AsReadOnly(offsets.ToArray());
    public IReadOnlyList<int> Bpms { get; } = Array.AsReadOnly(bpms.ToArray());
    public TimeSpan InitialPosition { get; } = initialPosition;
    public int InitialSubdivision { get; } = initialSubdivision;
    public double InitialZoomFactor { get; } = initialZoomFactor;
    public int SongVolume { get; } = songVolume;
    public int SamplesVolume { get; } = samplesVolume;
    public int ApplicationVolume { get; } = applicationVolume;
}
