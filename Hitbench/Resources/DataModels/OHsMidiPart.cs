namespace Hitbench.Resources.DataModels;

public sealed class OHsMidiPart(int trackIndex, int channel, string? name, IReadOnlyList<OHsMidiAttack> attacks)
{
    public int TrackIndex { get; } = trackIndex;
    public int Channel { get; } = channel;
    public string? Name { get; } = name;
    public IReadOnlyList<OHsMidiAttack> Attacks { get; } = Array.AsReadOnly(attacks.ToArray());
}
