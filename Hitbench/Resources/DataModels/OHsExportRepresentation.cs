using System.Collections.ObjectModel;
namespace Hitbench.Resources.DataModels;
public sealed record OHsExportRepresentation
{
    public bool IsLinear { get; }
    public int NativeVolume { get; }
    public string NormalSet { get; }
    public string AdditionSet { get; }
    public int HitSound { get; }
    public IReadOnlyDictionary<OHsExportSlot, OHsExportAsset> Slots { get; }
    public int MergeCount { get; }
    public int MixedComponentCount { get; }
    public OHsExportRepresentation(string normalSet, string additionSet, int hitSound, IReadOnlyDictionary<OHsExportSlot, OHsExportAsset> slots, int mergeCount, int mixedComponentCount, int nativeVolume = 100, bool isLinear = false)
    {
        if (nativeVolume is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(nativeVolume));
        NativeVolume = nativeVolume; IsLinear = isLinear;
        NormalSet = normalSet; AdditionSet = additionSet; HitSound = hitSound;
        Slots = new ReadOnlyDictionary<OHsExportSlot, OHsExportAsset>(slots.ToDictionary());
        MergeCount = mergeCount; MixedComponentCount = mixedComponentCount;
    }
}
