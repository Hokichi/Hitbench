namespace Hitbench.Resources.DataModels;
public sealed record OHsExportInput
{
    public OHsExportOptions Options { get; }
    public OHsExportBeatmap Beatmap { get; }
    public IReadOnlyList<OHsExportVoice> Voices { get; }
    public IReadOnlyList<OHsExportTimingPoint> RequiredPoints { get; }
    public OHsExportInput(OHsExportOptions options, OHsExportBeatmap beatmap, IReadOnlyList<OHsExportVoice> voices, IReadOnlyList<OHsExportTimingPoint> requiredPoints)
    { Options = options; Beatmap = beatmap; Voices = Array.AsReadOnly(voices.ToArray()); RequiredPoints = Array.AsReadOnly(requiredPoints.ToArray()); }
}
