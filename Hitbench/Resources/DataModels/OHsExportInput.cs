namespace Hitbench.Resources.DataModels;
public sealed record OHsExportInput
{
    public IReadOnlyList<Hitbench.Core.Entities.ControlPointVolumeSnapshot> ControlPoints { get; }
    public OHsExportOptions Options { get; }
    public OHsExportBeatmap Beatmap { get; }
    public IReadOnlyList<OHsExportVoice> Voices { get; }
    public IReadOnlyList<OHsExportTimingPoint> RequiredPoints { get; }
    public OHsExportInput(OHsExportOptions options, OHsExportBeatmap beatmap, IReadOnlyList<OHsExportVoice> voices, IReadOnlyList<OHsExportTimingPoint> requiredPoints, IReadOnlyList<Hitbench.Core.Entities.ControlPointVolumeSnapshot>? controlPoints = null)
    { ControlPoints = Array.AsReadOnly(controlPoints?.ToArray() ?? []); Options = options; Beatmap = beatmap; Voices = Array.AsReadOnly(voices.ToArray()); RequiredPoints = Array.AsReadOnly(requiredPoints.ToArray()); }
}
