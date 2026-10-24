using Onset.Resources.DataModels;
namespace Onset.Services;

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
        var prepared = 0;
        var groups = timestamps.Select(g =>
        {
            token.ThrowIfCancellationRequested();
            var voices = g.ToArray();
            var key = string.Join('|', voices.Select(v => $"{v.Sampleset}:{v.Type}:{v.GainPercent}:{v.FileName.Length}:{v.FileName.ToUpperInvariant()}").Order(StringComparer.Ordinal));
            if (!recipes.TryGetValue(key, out var choices)) recipes[key] = choices = OHsExportRepresentations.Build(voices, token, assets);
            progress?.Report(new(10 + 15d * ++prepared / timestamps.Count, "Preparing sample combinations"));
            return (Time: g[0].Timestamp, Choices: choices);
        }).ToArray();
        var required = input.RequiredPoints.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind).ToArray();
        var start = input.Options.StartingIndex;
        if (groups.Length == 0) return new([], required.Select(p => p with { SampleIndex = start }).ToArray(), new Dictionary<int, IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>>(), 0);
        var intervalPlan = PlanIntervals(groups, required, start, token, out var lowerBounds, out var minimum);
        return PlanBanks(groups, required, start, token, lowerBounds, minimum, intervalPlan);
    }
}
