namespace Soundie.Resources.DataModels;

public sealed class OHsTimingSnapshot(IReadOnlyList<int> offsets, IReadOnlyList<int> bpms)
{
    public IReadOnlyList<int> Offsets { get; } = Array.AsReadOnly(offsets.ToArray());
    public IReadOnlyList<int> Bpms { get; } = Array.AsReadOnly(bpms.ToArray());
}
