namespace Hitbench.Resources.DataModels;

public sealed class OHsMidiImportSource(string fileName, int ticksPerQuarterNote, bool hasExplicitTempo,
    IReadOnlyList<OHsMidiTempoEvent> tempos, IReadOnlyList<OHsMidiPart> parts,
    int excludedPitchCount, IReadOnlyList<string> excludedPercussionParts)
{
    public string FileName { get; } = fileName;
    public int TicksPerQuarterNote { get; } = ticksPerQuarterNote;
    public bool HasExplicitTempo { get; } = hasExplicitTempo;
    public IReadOnlyList<OHsMidiTempoEvent> Tempos { get; } = Array.AsReadOnly(tempos.ToArray());
    public IReadOnlyList<OHsMidiPart> Parts { get; } = Array.AsReadOnly(parts.ToArray());
    public int ExcludedPitchCount { get; } = excludedPitchCount;
    public IReadOnlyList<string> ExcludedPercussionParts { get; } = Array.AsReadOnly(excludedPercussionParts.ToArray());
}
