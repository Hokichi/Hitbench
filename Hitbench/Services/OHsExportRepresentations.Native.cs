using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;

namespace Hitbench.Services;

public static partial class OHsExportRepresentations
{
    public static OHsExportRepresentation? TryBuildNative(IReadOnlyList<OHsExportVoice> voices, int timingVolume)
    {
        if (voices.Count == 0 || voices.Any(v => !v.IsDefault) || voices.Select(v => v.GainPercent).Distinct().Count() != 1) return null;
        var normal = voices.Where(v => v.Type == SampleType.Normal).ToArray();
        // osu! always layers a normal hit: without an authored normal voice, render a custom mix to avoid adding sound.
        if (normal.Length != 1 || voices.GroupBy(v => v.Type).Any(g => g.Count() != 1)) return null;
        var additions = voices.Where(v => v.Type != SampleType.Normal).ToArray();
        if (additions.Select(v => v.Sampleset).Distinct().Count() > 1) return null;
        var gain = voices[0].GainPercent;
        var volume = (decimal)timingVolume * gain / 100;
        if (gain != 100 && (volume < 1 || volume > 100 || decimal.Truncate(volume) != volume)) return null;
        var flags = voices.Aggregate(0, (bits, v) => bits | (v.Type switch
        { SampleType.Normal => 1, SampleType.Whistle => 2, SampleType.Finish => 4, SampleType.Clap => 8, _ => 0 }));
        return new(normal[0].Sampleset, additions.FirstOrDefault()?.Sampleset ?? normal[0].Sampleset, flags,
            new Dictionary<OHsExportSlot, OHsExportAsset>(), 0, 0, isNative: true, volume: gain == 100 ? 0 : (int)volume);
    }
}
