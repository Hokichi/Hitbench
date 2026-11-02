using System.IO;
using Hitbench.Resources.DataModels;

namespace Hitbench.Services;

internal sealed partial class OHsExportLayoutSearch
{
    internal static OHsExportPlan FindTimeline((int Time, IReadOnlyList<OHsExportRepresentation> Choices)[] groups,
        OHsExportTimingPoint[] required, int start, CancellationToken token, IProgress<OHsExportProgress>? progress, Func<OHsExportPlan, bool>? accept = null, int[]? preferredVolumes = null)
    {
        var capacity = 1000 - start;
        var banks = new List<Dictionary<OHsExportSlot, OHsExportAsset>>();
        var selected = new (int Bank, OHsExportRepresentation Choice)[groups.Length];
        var memo = new Dictionary<string, (int Rows, string Prefix)>(StringComparer.Ordinal);
        OHsExportPlan? best = null;
        string? bestKey = null;
        var bestDistance = int.MaxValue;
        var hasLayout = false;
        var freeSwitch = Enumerable.Range(0, groups.Length).Select(i => i > 0 && required.Any(p =>
            p.Timestamp > (decimal)groups[i - 1].Time + 5 && p.Timestamp <= (decimal)groups[i].Time + 5)).ToArray();
        var ordered = new Dictionary<(IReadOnlyList<OHsExportRepresentation> Choices, int Preferred), OHsExportRepresentation[]>();
        var choices = groups.Select((g, i) =>
        {
            var key = (g.Choices, preferredVolumes?[i] ?? 100);
            if (!ordered.TryGetValue(key, out var result))
                ordered[key] = result = g.Choices.OrderBy(c => c.Slots.Count).ThenBy(c => Math.Abs(c.NativeVolume - key.Item2))
                    .ThenBy(c => SlotKey(c.Slots), StringComparer.Ordinal).ThenBy(ChoiceKey, StringComparer.Ordinal).ToArray();
            return result;
        }).ToArray();
        var repeated = Enumerable.Range(0, groups.Length).Select(i => i > 0 && (ReferenceEquals(choices[i], choices[i - 1]) ||
            string.Join('|', choices[i].Select(ChoiceKey)) == string.Join('|', choices[i - 1].Select(ChoiceKey)))).ToArray();
        if (choices.Any(c => c.Length == 0)) throw new InvalidOperationException("No lossless export representation is available.");
        var requirements = BuildTimelineRequirements(choices, token);
        int Bound(int files) => files + requirements.GroupBy(r => r.Slot).Sum(slot => slot.Max(r =>
            Math.Max(0, r.Count - banks.Count(b => b.TryGetValue(r.Slot, out var asset) && r.Ids.Contains(asset.Id)))));
        var forced = new int[groups.Length + 1];
        var distanceSuffix = new int[groups.Length + 1];
        for (var n = groups.Length - 1; n >= 0; n--)
        {
            token.ThrowIfCancellationRequested();
            distanceSuffix[n] = distanceSuffix[n + 1] + choices[n].Min(c => Math.Abs(c.NativeVolume - (preferredVolumes?[n] ?? 100)));
            forced[n] = forced[n + 1] + (n > 0 && !freeSwitch[n] && !choices[n - 1].Any(a => choices[n].Any(b =>
                a.NativeVolume == b.NativeVolume && Compatible(a.Slots, b.Slots))) ? 1 : 0);
        }
        var lastProgress = System.Diagnostics.Stopwatch.GetTimestamp();
        void CheckSearch()
        {
            token.ThrowIfCancellationRequested();
            if (progress is not null && System.Diagnostics.Stopwatch.GetElapsedTime(lastProgress).TotalMilliseconds >= 250)
            {
                progress.Report(new(35, best is null ? "Optimizing sample reuse" : $"Optimizing sample reuse ({best.AudioFileCount} files)"));
                lastProgress = System.Diagnostics.Stopwatch.GetTimestamp();
                token.ThrowIfCancellationRequested();
            }
        }
        // Seed a valid upper bound; all alternatives remain in the exact search.
        for (var i = 0; i < groups.Length; i++)
        {
            var options = Options(i).OrderBy(o => o.Added).ThenBy(o => SwitchCost(i, o.Bank, o.Choice)).ToArray();
            if (options.Length == 0) break;
            Apply(options[0]); selected[i] = (options[0].Bank, options[0].Choice);
            if (i == groups.Length - 1) Save();
        }
        banks.Clear();
        var index = 0;
        if (Visit(ref index, 0, 0, out var initial))
        {
            var stack = new Stack<(int Index, int Files, int Rows, (int Bank, OHsExportRepresentation Choice, int Added)[] Options, int Next, int Bank, Dictionary<OHsExportSlot, OHsExportAsset>? Previous, bool New)>();
            stack.Push((index, 0, 0, initial, 0, -1, null, false));
            while (stack.TryPop(out var frame))
            {
                CheckSearch();
                if (frame.Next == frame.Options.Length)
                {
                    if (frame.Bank >= 0) Undo(frame.Bank, frame.Previous, frame.New);
                    continue;
                }
                stack.Push(frame with { Next = frame.Next + 1 });
                var option = frame.Options[frame.Next];
                var rows = frame.Rows + SwitchCost(frame.Index, option.Bank, option.Choice);
                var isNew = option.Bank == banks.Count;
                var previous = isNew ? null : new Dictionary<OHsExportSlot, OHsExportAsset>(banks[option.Bank]);
                Apply(option); selected[frame.Index] = (option.Bank, option.Choice);
                var nextIndex = frame.Index + 1;
                if (Visit(ref nextIndex, frame.Files + option.Added, rows, out var nextOptions))
                    stack.Push((nextIndex, frame.Files + option.Added, rows, nextOptions, 0, option.Bank, previous, isNew));
                else Undo(option.Bank, previous, isNew);
            }
        }
        CheckSearch();
        if (best is not null) return best;
        if (!hasLayout) throw new InvalidOperationException(IndexRangeError);
        throw new InvalidDataException("No lossless export representation fits this arrangement.");

        static string SlotKey(IReadOnlyDictionary<OHsExportSlot, OHsExportAsset> slots) =>
            string.Join('|', slots.OrderBy(p => p.Key.Sampleset).ThenBy(p => p.Key.Type).Select(p => p.Key + ":" + p.Value.Id));
        static string ChoiceKey(OHsExportRepresentation c) => c.NativeVolume + ":" + SlotKey(c.Slots);
        string BanksKey() => string.Join(';', banks.Select(b => string.Join('|', b.OrderBy(p => p.Key.Sampleset).ThenBy(p => p.Key.Type).Select(p => p.Key + ":" + p.Value.Id))));
        int SwitchCost(int i, int bank, OHsExportRepresentation choice) => i == 0 ? (required.Length == 0 ? 1 : 0)
            : freeSwitch[i] || selected[i - 1].Bank == bank && selected[i - 1].Choice.NativeVolume == choice.NativeVolume ? 0 : 1;
        IEnumerable<(int Bank, OHsExportRepresentation Choice, int Added)> Options(int i)
        {
            // With linear audio, delay a paid switch inside an identical run until it is needed.
            // Added bank slots can likewise be postponed without changing physical cost.
            if (accept is null && i > 0 && repeated[i] && !freeSwitch[i])
            { yield return (selected[i - 1].Bank, selected[i - 1].Choice, 0); yield break; }
            foreach (var choice in choices[i])
                for (var bank = 0; bank <= banks.Count && bank < capacity; bank++)
                {
                    CheckSearch();
                    if (bank < banks.Count && choice.Slots.Any(p => banks[bank].TryGetValue(p.Key, out var existing) && existing.Id != p.Value.Id)) continue;
                    yield return (bank, choice, bank == banks.Count ? choice.Slots.Count : choice.Slots.Count(p => !banks[bank].ContainsKey(p.Key)));
                }
        }
        void Apply((int Bank, OHsExportRepresentation Choice, int Added) option)
        {
            if (option.Bank == banks.Count) banks.Add(new());
            foreach (var pair in option.Choice.Slots) banks[option.Bank][pair.Key] = pair.Value;
        }
        void Save()
        {
            var plan = OHsExportPlanner.BuildSelectedTimeline(groups, selected, banks.Select(b => (IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>)b).ToArray(), required, start, token);
            hasLayout = true;
            var distance = selected.Select((s, i) => Math.Abs(s.Choice.NativeVolume - (preferredVolumes?[i] ?? 100))).Sum();
            var key = distance.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + ":" + BanksKey() + ":" + string.Join('|', selected.Select(s => s.Bank + ":" + ChoiceKey(s.Choice)));
            if (best is null || plan.AudioFileCount < best.AudioFileCount || plan.AudioFileCount == best.AudioFileCount &&
                (plan.AdditionalPointCount < best.AdditionalPointCount || plan.AdditionalPointCount == best.AdditionalPointCount && string.CompareOrdinal(key, bestKey) < 0))
            {
                if (accept is not null && !accept(plan)) return;
                CheckSearch();
                best = plan; bestKey = key; bestDistance = distance;
                progress?.Report(new(35, $"Optimizing sample reuse ({plan.AudioFileCount} files)"));
            }
        }
        void Undo(int bank, Dictionary<OHsExportSlot, OHsExportAsset>? previous, bool isNew)
        { if (isNew) banks.RemoveAt(banks.Count - 1); else banks[bank] = previous!; }
        bool ImmutablePrefixIsWorse(int i)
        {
            var stable = new List<string>();
            // Keep the last bank out: a completion can append another bank or end the key.
            for (var b = 0; b + 1 < banks.Count; b++)
            {
                CheckSearch();
                if (choices.Skip(i).SelectMany(c => c).Any(c => Compatible(banks[b], c.Slots) && c.Slots.Keys.Any(k => !banks[b].ContainsKey(k)))) break;
                stable.Add(string.Join('|', banks[b].OrderBy(p => p.Key.Sampleset).ThenBy(p => p.Key.Type).Select(p => p.Key + ":" + p.Value.Id)));
            }
            if (stable.Count == 0) return false;
            var prefix = string.Join(';', stable) + ";";
            var incumbent = bestKey![(bestKey!.IndexOf(':') + 1)..];
            return string.CompareOrdinal(prefix, incumbent[..Math.Min(prefix.Length, incumbent.Length)]) > 0;
        }
        bool Visit(ref int i, int files, int rows, out (int Bank, OHsExportRepresentation Choice, int Added)[] options)
        {
            CheckSearch();
            options = [];
            var bound = Bound(files);
            var rowBound = rows + forced[i];
            if (best is not null && (bound > best.AudioFileCount || bound == best.AudioFileCount && rowBound > best.AdditionalPointCount)) return false;
            while (accept is null && i > 0 && i < groups.Length && repeated[i] && !freeSwitch[i])
            { CheckSearch(); selected[i] = selected[i - 1]; i++; }
            var distance = selected.Take(i).Select((s, n) => Math.Abs(s.Choice.NativeVolume - (preferredVolumes?[n] ?? 100))).Sum();
            if (best is not null && bound == best.AudioFileCount && rowBound == best.AdditionalPointCount && distance + distanceSuffix[i] > bestDistance) return false;
            if (best is not null && bound == best.AudioFileCount && rowBound == best.AdditionalPointCount &&
                distance + distanceSuffix[i] == bestDistance && ImmutablePrefixIsWorse(i)) return false;
            if (i == groups.Length) { Save(); return false; }
            if (accept is null)
            {
                var key = i + ":" + BanksKey() + ":" + (i == 0 ? "" : selected[0].Choice.NativeVolume + ":" + selected[i - 1].Bank + ":" + selected[i - 1].Choice.NativeVolume);
                var prefix = distance.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + ":" + string.Join('|', selected.Take(i).Select(s => s.Bank + ":" + ChoiceKey(s.Choice)));
                if (memo.TryGetValue(key, out var prior) && (prior.Rows < rows || prior.Rows == rows && string.CompareOrdinal(prior.Prefix, prefix) <= 0)) return false;
                memo[key] = (rows, prefix);
            }
            var current = i;
            options = Options(i).OrderBy(o => o.Added).ThenBy(o => SwitchCost(current, o.Bank, o.Choice)).ToArray();
            return options.Length > 0;
        }
    }
}
