using System.Collections.ObjectModel;
namespace Onset.Resources.DataModels;
public sealed record OHsExportPlan
{
    public IReadOnlyList<OHsExportCircle> Circles { get; }
    public IReadOnlyList<OHsExportTimingPoint> TimingPoints { get; }
    public IReadOnlyDictionary<int, IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>> Banks { get; }
    public int AudioFileCount => Banks.Values.Sum(bank => bank.Count);
    public int AdditionalPointCount { get; }
    public OHsExportPlan(IReadOnlyList<OHsExportCircle> circles, IReadOnlyList<OHsExportTimingPoint> timingPoints,
        IReadOnlyDictionary<int, IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>> banks, int additionalPointCount)
    {
        Circles = Array.AsReadOnly(circles.ToArray()); TimingPoints = Array.AsReadOnly(timingPoints.ToArray());
        Banks = new ReadOnlyDictionary<int, IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>>(banks.ToDictionary(p => p.Key,
            p => (IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>)new ReadOnlyDictionary<OHsExportSlot, OHsExportAsset>(p.Value.ToDictionary())));
        AdditionalPointCount = additionalPointCount;
    }
}
