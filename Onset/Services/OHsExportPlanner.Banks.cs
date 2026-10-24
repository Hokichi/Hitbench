using Onset.Resources.DataModels;

namespace Onset.Services;

public static partial class OHsExportPlanner
{
    private static OHsExportPlan PlanBanks((int Time, IReadOnlyList<OHsExportRepresentation> Choices)[] groups,
        OHsExportTimingPoint[] required, int start, CancellationToken token, int[] lowerBounds, int minimum)
    {
        var pending = new Stack<(int Next, int Active, Dictionary<int, Dictionary<OHsExportSlot, OHsExportAsset>> Banks, List<OHsExportCircle> Circles, List<(decimal Time, int Bank, bool Added)> Switches, int Cost)>();
        pending.Push((0, start, new(), [], [], 0));
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        OHsExportPlan? best = null;
        while (pending.TryPop(out var state))
        {
            token.ThrowIfCancellationRequested();
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
                if (best is null || count < best.AdditionalPointCount)
                {
                    best = new(state.Circles, rows, state.Banks.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>)p.Value), count);
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
