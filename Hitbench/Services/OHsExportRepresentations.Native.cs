using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;

namespace Hitbench.Services;

public static partial class OHsExportRepresentations
{
    public static OHsExportRepresentation? TryBuildNative(IReadOnlyList<OHsExportVoice> voices, int timingVolume, string fallbackSet = "Normal", string fallbackPath = "")
    {
        if (voices.Count == 0 || voices.Any(v => !v.IsDefault) || voices.Select(v => v.GainPercent).Distinct().Count() != 1) return null;
        var normal = voices.Where(v => v.Type == SampleType.Normal).ToArray();
        if (normal.Length > 1 || normal.Length == 0 && (fallbackPath.Length > 0 || voices[0].GainPercent != 100) || voices.GroupBy(v => v.Type).Any(g => g.Count() != 1)) return null;
        var additions = voices.Where(v => v.Type != SampleType.Normal).ToArray();
        if (additions.Select(v => v.Sampleset).Distinct().Count() > 1) return null;
        var gain = voices[0].GainPercent;
        var volume = (decimal)timingVolume * gain / 100;
        if (gain != 100 && (volume < 1 || volume > 100 || decimal.Truncate(volume) != volume)) return null;
        var flags = voices.Aggregate(0, (bits, v) => bits | (v.Type switch
        { SampleType.Normal => 1, SampleType.Whistle => 2, SampleType.Finish => 4, SampleType.Clap => 8, _ => 0 }));
        var normalSet = normal.FirstOrDefault()?.Sampleset ?? fallbackSet;
        return new(normalSet, additions.FirstOrDefault()?.Sampleset ?? normalSet, flags,
            new Dictionary<OHsExportSlot, OHsExportAsset>(), 0, 0, isNative: true, volume: gain == 100 ? 0 : (int)volume);
    }
}
