using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
namespace Hitbench.Services;

public static partial class OHsExportPlanner
{
    public static OHsExportPlan Plan(OHsExportInput input, CancellationToken token, IProgress<OHsExportProgress>? progress = null, Func<string, DecodedAudio>? resolve = null)
        => Plan(input, token, progress, resolve, default);

    internal static OHsExportPlan Plan(OHsExportInput input, CancellationToken token, IProgress<OHsExportProgress>? progress, Func<string, DecodedAudio>? resolve, CancellationToken budgetCancellation)
    {
        token.ThrowIfCancellationRequested(); input.Options.Validate();
        var assets = new Dictionary<string, OHsExportAsset>(StringComparer.Ordinal);
        var recipes = new Dictionary<string, IReadOnlyList<OHsExportRepresentation>>(StringComparer.Ordinal);
        var normals = input.Voices.Where(v => v.Type == SampleType.Normal).ToArray();
        var timestamps = new List<List<OHsExportVoice>>();
        foreach (var voice in input.Voices.OrderBy(v => v.Timestamp))
        {
            token.ThrowIfCancellationRequested();
            if (timestamps.Count == 0 || (long)voice.Timestamp - timestamps[^1][0].Timestamp > 1)
                timestamps.Add([]);
            timestamps[^1].Add(voice);
        }
        var timeline = new ControlPointVolumeTimeline(input.ControlPoints);
        // Automation-induced silence can reuse the same physical normal slot as explicit zero gain.
        var authoredSilence = normals.Any(v => v.GainPercent == 0) || resolve is not null && timestamps.Any(group =>
            timeline.GetGain(ControlPointVolumeTimeline.ToFrame(group[0].Timestamp)) == 0 && group.Any(v =>
                v.Type == SampleType.Normal && OHsExportEnvelope.Create(timeline,
                    ControlPointVolumeTimeline.ToFrame(group[0].Timestamp), resolve(v.FileName).FrameCount).Segments
                    .All(segment => segment.StartNumerator.IsZero && segment.SlopeNumerator.IsZero)));
        var support = authoredSilence ? null : new[] { "Normal", "Soft", "Drum" }
            .FirstOrDefault(set => normals.All(v => v.Sampleset != set));
        var shapedRecipes = new Dictionary<string, IReadOnlyList<OHsExportRepresentation>>(StringComparer.Ordinal);
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
            if (resolve is not null)
            {
                var envelopeKey = key + ":" + string.Join('|', voices.Select(v => OHsExportEnvelope.Create(timeline,
                    ControlPointVolumeTimeline.ToFrame(g[0].Timestamp), resolve(v.FileName).FrameCount).Key));
                if (!shapedRecipes.TryGetValue(envelopeKey, out var shaped))
                    shapedRecipes[envelopeKey] = shaped = OHsExportRepresentations.Shape(choices, g[0].Timestamp, timeline, resolve, assets, token);
                choices = shaped;
            }
            else if (input.ControlPoints.Any(p => p.IsRamp)) throw new InvalidOperationException("Export envelopes require decoded source durations.");
            progress?.Report(new((resolve is null ? 10 : 25) + (resolve is null ? 15d : 10d) * ++prepared / timestamps.Count, "Preparing sample combinations"));
            return (Time: g[0].Timestamp, Choices: choices);
        }).ToArray();
        var required = input.RequiredPoints.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind).ToArray();
        var start = input.Options.StartingIndex;
        if (groups.Length == 0) return new([], required.Select(p => p with { SampleIndex = start }).ToArray(), new Dictionary<int, IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>>(), 0);
        if (resolve is not null) return OHsExportLayoutSearch.FindTimeline(groups, required, start, token, progress,
            groups.All(g => g.Choices.All(c => c.IsLinear)) ? null : (plan, searchToken) => OHsExportAudio.IsPlaybackEquivalent(input, plan, resolve, searchToken),
            groups.Select(g => (int)Math.Round(timeline.GetVolumeAtTimestamp(g.Time), MidpointRounding.AwayFromZero)).ToArray(), budgetCancellation);
        if (input.ControlPoints.Count > 0)
        {
            var fixedGroups = groups.Select(g =>
            {
                var volume = (int)Math.Round(timeline.GetVolumeAtTimestamp(g.Time), MidpointRounding.AwayFromZero);
                return (g.Time, Choices: (IReadOnlyList<OHsExportRepresentation>)g.Choices.Select(c => new OHsExportRepresentation(
                    c.NormalSet, c.AdditionSet, c.HitSound, c.Slots, c.MergeCount, c.MixedComponentCount, volume)).ToArray());
            }).ToArray();
            return OHsExportLayoutSearch.FindTimeline(fixedGroups, required, start, token, progress,
                preferredVolumes: fixedGroups.Select(g => g.Choices[0].NativeVolume).ToArray(), budgetCancellation: budgetCancellation);
        }
        var layout = OHsExportLayoutSearch.Find(recipes.Values.ToArray(), 1000 - start, token, progress, budgetCancellation);
        return BuildTimeline(groups, required, start, layout, token);
    }
}
