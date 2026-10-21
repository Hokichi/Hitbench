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
        var pending = new Stack<(int Next, int Active, Dictionary<int, Dictionary<OHsExportSlot, OHsExportAsset>> Banks, List<OHsExportCircle> Circles, List<(decimal Time, int Bank, bool Added)> Switches, int Cost)>();
        pending.Push((0, start, new(), [], [], 0));
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        OHsExportPlan? best = null; var bestFiles = int.MaxValue; var bestEncodings = int.MaxValue;
        while (pending.TryPop(out var state))
        {
            token.ThrowIfCancellationRequested();
            var files = state.Banks.Values.Sum(b => b.Count);
            if (best is not null && state.Cost + lowerBounds[state.Next] >= best.AdditionalPointCount) continue;
            if (state.Next == groups.Length)
            {
                var rows = Schedule(state.Switches);
                var count = state.Cost;
                var first = rows.FirstOrDefault();
                // Legacy sample lookup uses the first physical row even before its offset.
                if (first is null || first.Timestamp > (decimal)groups[0].Time + 5 && (first.SampleIndex != start || first.SampleSet != 1 || first.Volume != 100))
                {
                    rows.Insert(0, new(groups[0].Time, "-100", 4, 1, start, 100, 0, OHsExportTimingKind.BankSwitch)); count++;
                }
                var encodings = state.Banks.Values.SelectMany(b => b.Values)
                    .Select(a => (a.Id, Format: a.Sources.Count > 1 ? input.Options.MixedFormat : input.Options.SingleFormat)).Distinct().Count();
                if (best is null || count < best.AdditionalPointCount || count == best.AdditionalPointCount &&
                    (files < bestFiles || files == bestFiles && encodings < bestEncodings))
                {
                    best = new(state.Circles, rows, state.Banks.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>)p.Value), count);
                    bestFiles = files; bestEncodings = encodings;
                    if (count == minimum) return best;
                }
                continue;
            }
            var key = state.Next + "/" + state.Active + "/" + string.Join(";", state.Banks.OrderBy(p => p.Key).Select(p => p.Key + ":" + string.Join(",", p.Value.OrderBy(s => s.Key.Sampleset).ThenBy(s => s.Key.Type).Select(s => s.Key + "=" + s.Value.Id)))) + "/" + state.Switches.FirstOrDefault();
            if (seen.TryGetValue(key, out var previous) && previous <= state.Cost) continue;
            seen[key] = state.Cost;
            var group = groups[state.Next];
            // Reverse pushes visit compatible existing banks before allocating new ones.
            var indices = state.Banks.Keys.Order().ToList();
            if (start + state.Banks.Count <= 999) indices.Add(start + state.Banks.Count);
            for (var choiceIndex = group.Choices.Count - 1; choiceIndex >= 0; choiceIndex--)
                foreach (var index in indices.AsEnumerable().Reverse())
                {
                    token.ThrowIfCancellationRequested(); var choice = group.Choices[choiceIndex];
                    state.Banks.TryGetValue(index, out var bank);
                    if (bank is not null && choice.Slots.Any(p => bank.TryGetValue(p.Key, out var asset) && asset.Id != p.Value.Id)) continue;
                    var banks = state.Banks.ToDictionary(p => p.Key, p => p.Value.ToDictionary());
                    var merged = bank?.ToDictionary() ?? new();
                    foreach (var slot in choice.Slots) merged[slot.Key] = slot.Value;
                    banks[index] = merged;
                    var switches = state.Switches.ToList(); var cost = state.Cost;
                    if (state.Next > 0 && index != state.Active)
                    {
                        var lower = (decimal)groups[state.Next - 1].Time + 5;
                        var upper = (decimal)group.Time + 5;
                        var opportunity = required.LastOrDefault(p => p.Timestamp > lower && p.Timestamp <= upper);
                        var time = opportunity?.Timestamp ?? (group.Time > lower ? group.Time : upper);
                        switches.Add((time, index, opportunity is null)); if (opportunity is null) cost++;
                    }
                    var circles = state.Circles.ToList(); circles.Add(new(group.Time, choice.NormalSet, choice.AdditionSet, choice.HitSound, index));
                    pending.Push((state.Next + 1, index, banks, circles, switches, cost));
                }
        }
        return best ?? throw new InvalidOperationException("The selected custom sample index range cannot represent this arrangement.");

        List<OHsExportTimingPoint> Schedule(List<(decimal Time, int Bank, bool Added)> switches)
        {
            var rows = required.ToList();
            foreach (var change in switches.Where(s => s.Added))
            {
                var preceding = required.LastOrDefault(p => p.Timestamp <= change.Time);
                rows.Add(new(change.Time, preceding is null || preceding.Kind == OHsExportTimingKind.SourceRed ? "-100" : preceding.BeatLength,
                    preceding?.Meter ?? 4, preceding?.SampleSet ?? 1, change.Bank, preceding?.Volume ?? 100, preceding?.Effects ?? 0, OHsExportTimingKind.BankSwitch));
            }
            return rows.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind).Select(p => p with { SampleIndex = switches.LastOrDefault(s => s.Time <= p.Timestamp, (Time: decimal.MinValue, Bank: start, Added: false)).Bank }).ToList();
        }
    }
}

