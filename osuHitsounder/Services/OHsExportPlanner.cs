using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Services;

public static partial class OHsExportPlanner
{
    public static OHsExportPlan Plan(OHsExportInput input, CancellationToken token, IProgress<OHsExportProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested(); input.Options.Validate();
        var assets = new Dictionary<string, OHsExportAsset>(StringComparer.Ordinal);
        var recipes = new Dictionary<string, IReadOnlyList<OHsExportRepresentation>>(StringComparer.Ordinal);
        var timestamps = input.Voices.GroupBy(v => v.Timestamp).OrderBy(g => g.Key).ToArray();
        var prepared = 0;
        var groups = timestamps.Select(g =>
        {
            token.ThrowIfCancellationRequested();
            var voices = g.ToArray();
            var key = string.Join('|', voices.Select(v => $"{v.Sampleset}:{v.Type}:{v.GainPercent}:{v.FileName.Length}:{v.FileName.ToUpperInvariant()}").Order(StringComparer.Ordinal));
            if (!recipes.TryGetValue(key, out var choices)) recipes[key] = choices = OHsExportRepresentations.Build(voices, token, assets);
            progress?.Report(new(10 + 15d * ++prepared / timestamps.Length, "Preparing sample combinations"));
            return (Time: g.Key, Choices: choices);
        }).ToArray();
        var required = input.RequiredPoints.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind).ToArray();
        var start = input.Options.StartingIndex;
        if (groups.Length == 0) return new([], required.Select(p => p with { SampleIndex = start }).ToArray(), new Dictionary<int, IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>>(), 0);
        var intervalPlan = PlanIntervals(groups, required, start, token, out var lowerBounds, out var minimum);
        if (intervalPlan is not null) return intervalPlan;
        return PlanBanks(groups, required, start, token, lowerBounds, minimum);
    }
}
