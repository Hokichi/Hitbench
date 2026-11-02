using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
namespace Hitbench.Services;

public static partial class OHsExportPlanner
{
    public static OHsExportPlan Plan(OHsExportInput input, CancellationToken token, IProgress<OHsExportProgress>? progress = null, Func<string, DecodedAudio>? resolve = null)
    {
        token.ThrowIfCancellationRequested(); input.Options.Validate();
        var assets = new Dictionary<string, OHsExportAsset>(StringComparer.Ordinal);
        var recipes = new Dictionary<string, IReadOnlyList<OHsExportRepresentation>>(StringComparer.Ordinal);
        var normals = input.Voices.Where(v => v.Type == SampleType.Normal).ToArray();
        var support = normals.Any(v => v.GainPercent == 0) ? null : new[] { "Normal", "Soft", "Drum" }
            .FirstOrDefault(set => normals.All(v => v.Sampleset != set));
        var timestamps = new List<List<OHsExportVoice>>();
        foreach (var voice in input.Voices.OrderBy(v => v.Timestamp))
        {
            token.ThrowIfCancellationRequested();
            if (timestamps.Count == 0 || (long)voice.Timestamp - timestamps[^1][0].Timestamp > 1)
                timestamps.Add([]);
            timestamps[^1].Add(voice);
        }
        var prepared = 0;
        var groups = timestamps.Select(g =>
        {
            token.ThrowIfCancellationRequested();
            var voices = g.ToArray();
            var key = string.Join('|', voices.Select(v => $"{v.Sampleset}:{v.Type}:{v.GainPercent}:{v.FileName.Length}:{v.FileName.ToUpperInvariant()}").Order(StringComparer.Ordinal));
            if (!recipes.TryGetValue(key, out var choices))
            {
                choices = OHsExportRepresentations.Build(voices, token, assets);
                if (support is not null && voices.All(v => v.Type != SampleType.Normal))
                    choices = Array.AsReadOnly(choices.Where(c => c.NormalSet == support).ToArray());
                recipes[key] = choices;
            }
            progress?.Report(new((resolve is null ? 10 : 25) + (resolve is null ? 15d : 10d) * ++prepared / timestamps.Count, "Preparing sample combinations"));
            return (Time: g[0].Timestamp, Choices: choices);
        }).ToArray();
        var required = BuildTimingPoints(input, groups.Select(g => g.Time).ToArray(), token);
        var start = input.Options.StartingIndex;
        if (groups.Length == 0) return new([], required.Select(p => p with { SampleIndex = start }).ToArray(), new Dictionary<int, IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>>(), 0);
        var volumeAt = CreateTimingVolumeResolver(input);
        if (resolve is null)
            return BuildTimeline(groups, required, start,
                OHsExportLayoutSearch.Find(recipes.Values.ToArray(), 1000 - start, token, progress), token, volumeAt);
        var clippingFree = OHsExportAudio.CanMixWithoutClipping(recipes.Values.SelectMany(r => r).SelectMany(c => c.Slots.Values), resolve, token);
        return OHsExportLayoutSearch.FindTimeline(groups, required, start, token, progress,
            clippingFree ? null : (plan, searchToken) => OHsExportAudio.IsPlaybackEquivalent(input, plan, resolve, searchToken), volumeAt);
    }
}
