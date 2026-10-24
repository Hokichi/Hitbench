using System.Collections.ObjectModel;
namespace Onset.Resources.DataModels;
public sealed record OHsExportRepresentation
{
    public string NormalSet { get; }
    public string AdditionSet { get; }
    public int HitSound { get; }
    public IReadOnlyDictionary<OHsExportSlot, OHsExportAsset> Slots { get; }
    public int MergeCount { get; }
    public int MixedComponentCount { get; }
    public OHsExportRepresentation(string normalSet, string additionSet, int hitSound, IReadOnlyDictionary<OHsExportSlot, OHsExportAsset> slots, int mergeCount, int mixedComponentCount)
    {
        NormalSet = normalSet; AdditionSet = additionSet; HitSound = hitSound;
        Slots = new ReadOnlyDictionary<OHsExportSlot, OHsExportAsset>(slots.ToDictionary());
        MergeCount = mergeCount; MixedComponentCount = mixedComponentCount;
    }
}
