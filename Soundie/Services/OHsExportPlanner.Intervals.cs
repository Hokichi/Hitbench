using Soundie.Resources.DataModels;

namespace Soundie.Services;

public static partial class OHsExportPlanner
{
    private static OHsExportPlan? PlanIntervals(
        (int Time, IReadOnlyList<OHsExportRepresentation> Choices)[] groups,
        OHsExportTimingPoint[] required, int start, CancellationToken token,
        out int[] lowerBounds, out int minimum)
    {
        var assets = new Dictionary<string, int>(StringComparer.Ordinal);
        int Slot(OHsExportSlot slot) => (slot.Sampleset switch { "Normal" => 0, "Soft" => 1, "Drum" => 2, _ => throw new ArgumentException("Unknown sampleset") }) * 4 + (int)slot.Type;
        var schemas = groups.Select(g => g.Choices.Select(c =>
        {
            var map = new int[12];
            foreach (var p in c.Slots)
            {
                if (!assets.TryGetValue(p.Value.Id, out var id)) assets[p.Value.Id] = id = assets.Count + 1;
                map[Slot(p.Key)] = id;
            }
            return map;
        }).ToArray()).ToArray();
        var n = groups.Length;
        var cuts = new (decimal Time, bool Added)[n];
        var row = 0;
        for (var i = 1; i < n; i++)
        {
            var lower = (decimal)groups[i - 1].Time + 5;
            var upper = (decimal)groups[i].Time + 5;
            while (row < required.Length && required[row].Timestamp <= upper) row++;
            var point = row > 0 && required[row - 1].Timestamp > lower ? required[row - 1] : null;
            cuts[i] = (point?.Timestamp ?? (groups[i].Time > lower ? groups[i].Time : upper), point is null);
        }
        var costs = new int[n + 1];
        var ends = new int[n];
        var witnesses = new OHsExportSchema?[n];
        var initialEnd = 0;
        OHsExportSchema? initialWitness = null;
        minimum = int.MaxValue;
        var first = required.FirstOrDefault();
        int Anchor(int end) => first is null ? 1 : first.Timestamp <= (decimal)groups[0].Time + 5 ? 0 :
            first.SampleSet != 1 || first.Volume != 100 || end < n && cuts[end].Time <= first.Timestamp ? 1 : 0;
        List<OHsExportSchema> Extend(List<OHsExportSchema> frontier, int to)
        {
            var next = new Dictionary<string, OHsExportSchema>(StringComparer.Ordinal);
            foreach (var previous in frontier)
                for (var choice = 0; choice < schemas[to].Length; choice++)
                {
                    token.ThrowIfCancellationRequested();
                    var map = schemas[to][choice];
                    var compatible = true;
                    for (var s = 0; s < 12; s++)
                        if (map[s] != 0 && previous.Slots[s] != 0 && map[s] != previous.Slots[s]) { compatible = false; break; }
                    if (!compatible) continue;
                    var union = (int[])previous.Slots.Clone();
                    for (var s = 0; s < 12; s++) if (map[s] != 0) union[s] = map[s];
                    next.TryAdd(string.Join(',', union), new(union, previous, groups[to].Choices[choice]));
                }
            return next.Values.ToList();
        }
        var whole = new List<OHsExportSchema> { new(new int[12], null, null) };
        for (var i = 0; i < n && whole.Count > 0; i++) whole = Extend(whole, i);
        if (whole.Count > 0)
        { initialEnd = n; initialWitness = whole[0]; minimum = Anchor(n); }
        for (var from = whole.Count == 0 ? n - 1 : -1; from >= 0; from--)
        {
            token.ThrowIfCancellationRequested();
            costs[from] = int.MaxValue;
            var frontier = new List<OHsExportSchema> { new(new int[12], null, null) };
            for (var to = from; to < n && frontier.Count > 0; to++)
            {
                frontier = Extend(frontier, to);
                if (frontier.Count == 0) break;
                var end = to + 1;
                var cost = end == n ? 0 : costs[end] + (cuts[end].Added ? 1 : 0);
                if (cost <= costs[from]) { costs[from] = cost; ends[from] = end; witnesses[from] = frontier[0]; }
                if (from == 0 && cost + Anchor(end) <= minimum)
                { minimum = cost + Anchor(end); initialEnd = end; initialWitness = frontier[0]; }
            }
        }
        lowerBounds = costs;
        var banks = new Dictionary<int, Dictionary<OHsExportSlot, OHsExportAsset>>();
        var circles = new List<OHsExportCircle>(n);
        var switches = new List<(decimal Time, int Bank, bool Added)>();
        var active = start;
        for (var from = 0; from < n;)
        {
            token.ThrowIfCancellationRequested();
            var end = from == 0 ? initialEnd : ends[from];
            var witness = from == 0 ? initialWitness : witnesses[from];
            var choices = new OHsExportRepresentation[end - from];
            for (var i = choices.Length - 1; i >= 0; i--) { choices[i] = witness!.Choice!; witness = witness.Previous; }
            var union = new Dictionary<OHsExportSlot, OHsExportAsset>();
            foreach (var choice in choices) foreach (var pair in choice.Slots) union[pair.Key] = pair.Value;
            var index = banks.Where(b => union.All(p => !b.Value.TryGetValue(p.Key, out var a) || a.Id == p.Value.Id))
                .OrderBy(b => union.Count(p => !b.Value.ContainsKey(p.Key))).ThenBy(b => b.Key).Select(b => b.Key).FirstOrDefault();
            if (index == 0)
            {
                index = start + banks.Count;
                if (index > 999) return null;
                banks[index] = new();
            }
            foreach (var pair in union) banks[index][pair.Key] = pair.Value;
            if (from > 0 && index != active) switches.Add((cuts[from].Time, index, cuts[from].Added));
            for (var i = from; i < end; i++)
            {
                var choice = choices[i - from];
                circles.Add(new(groups[i].Time, choice.NormalSet, choice.AdditionSet, choice.HitSound, index));
            }
            active = index; from = end;
        }
        var rows = required.ToList();
        foreach (var change in switches.Where(s => s.Added))
        {
            var preceding = required.LastOrDefault(p => p.Timestamp <= change.Time);
            rows.Add(new(change.Time, preceding is null || preceding.Kind == OHsExportTimingKind.SourceRed ? "-100" : preceding.BeatLength,
                preceding?.Meter ?? 4, preceding?.SampleSet ?? 1, change.Bank, preceding?.Volume ?? 100, preceding?.Effects ?? 0, OHsExportTimingKind.BankSwitch));
        }
        rows = rows.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind).Select(p => p with
        { SampleIndex = switches.LastOrDefault(s => s.Time <= p.Timestamp, (Time: decimal.MinValue, Bank: start, Added: false)).Bank }).ToList();
        var added = switches.Count(s => s.Added);
        first = rows.FirstOrDefault();
        if (first is null || first.Timestamp > (decimal)groups[0].Time + 5 && (first.SampleIndex != start || first.SampleSet != 1 || first.Volume != 100))
        { rows.Insert(0, new(groups[0].Time, "-100", 4, 1, start, 100, 0, OHsExportTimingKind.BankSwitch)); added++; }
        return new(circles, rows, banks.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>)p.Value), added);
    }
}
