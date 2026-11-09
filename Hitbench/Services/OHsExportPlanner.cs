using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
namespace Hitbench.Services;

public static partial class OHsExportPlanner
{
    public static OHsExportPlan Plan(OHsExportInput input, CancellationToken token, IProgress<OHsExportProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested(); input.Options.Validate();
        var assets = new Dictionary<string, OHsExportAsset>(StringComparer.Ordinal);
        var recipes = new Dictionary<string, IReadOnlyList<OHsExportRepresentation>>(StringComparer.Ordinal);
        var timestamps = new List<List<OHsExportVoice>>();
        foreach (var voice in input.Voices.OrderBy(v => v.Timestamp))
        {
            token.ThrowIfCancellationRequested();
            if (timestamps.Count == 0 || (long)voice.Timestamp - timestamps[^1][0].Timestamp > 1)
                timestamps.Add([]);
            timestamps[^1].Add(voice);
        }
        var timingVolume = CreateTimingVolumeResolver(input);
        var samplesetAt = CreateTimingSamplesetResolver(input);
        var prepared = 0;
        var groups = timestamps.Select(g =>
        {
            token.ThrowIfCancellationRequested();
            var voices = g.ToArray();
            var fallbackSet = samplesetAt((decimal)g[0].Timestamp + 5);
            var fallbackPath = input.HitNormals.GetPath(fallbackSet);
            var native = OHsExportRepresentations.TryBuildNative(voices, timingVolume((decimal)g[0].Timestamp + 5), fallbackSet, fallbackPath);
            if (native is not null) return (Time: g[0].Timestamp, Choices: (IReadOnlyList<OHsExportRepresentation>)Array.AsReadOnly(new[] { native }));
            var key = fallbackSet + ":" + fallbackPath.Length + ":" + fallbackPath + "|" + string.Join('|', voices.Select(v => $"{v.Sampleset}:{v.Type}:{v.GainPercent}:{v.FileName.Length}:{v.FileName.ToUpperInvariant()}").Order(StringComparer.Ordinal));
            if (!recipes.TryGetValue(key, out var choices))
            {
                choices = OHsExportRepresentations.Build(voices, token, assets, fallbackSet, fallbackPath);
                recipes[key] = choices;
            }
            progress?.Report(new(25 + 10d * ++prepared / timestamps.Count, "Preparing sample combinations"));
            return (Time: g[0].Timestamp, Choices: choices);
        }).ToArray();
        var start = input.Options.StartingIndex;
        if (groups.Length == 0) return new([], BuildTimingPoints(input, [], token).Select(p => p with { SampleIndex = start }).ToArray(), new Dictionary<int, IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>>(), 0);
        var layout = recipes.Count == 0 ? Array.Empty<IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>>() : OHsExportLayoutSearch.Find(recipes.Values.ToArray(), 1000 - start, token, progress);
        var required = BuildTimingPoints(input, groups.Select(g => g.Time).ToArray(), token);
        return BuildTimeline(groups, required, start, layout, token, CreateTimingVolumeResolver(input));
    }
}
