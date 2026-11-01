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
        var choices = groups.Select((g, i) => g.Choices.OrderBy(c => c.Slots.Count).ThenBy(c => Math.Abs(c.NativeVolume - (preferredVolumes?[i] ?? 100)))
            .ThenBy(c => ChoiceKey(c), StringComparer.Ordinal).ToArray()).ToArray();
        var repeated = Enumerable.Range(0, groups.Length).Select(i => i > 0 &&
            string.Join('|', choices[i].Select(ChoiceKey)) == string.Join('|', choices[i - 1].Select(ChoiceKey))).ToArray();
        static string SourceIdentity(OHsExportAsset asset) => string.Join('|', asset.Components.Select(s => s.FileName.ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal));
        if (choices.Any(c => c.Length == 0)) throw new InvalidOperationException("No lossless export representation is available.");
        var mandatory = groups.SelectMany(g =>
        {
            var common = g.Choices[0].Slots.Select(p => (p.Key, Identity: SourceIdentity(p.Value))).ToHashSet();
            foreach (var choice in g.Choices.Skip(1)) common.IntersectWith(choice.Slots.Select(p => (p.Key, Identity: SourceIdentity(p.Value))));
            return common;
        }).Distinct().ToArray();
        int Bound(int files) => files + mandatory.Count(pair => !banks.Any(b => b.TryGetValue(pair.Key, out var asset) && SourceIdentity(asset) == pair.Identity));
        if (choices.Any(c => c.Length == 0)) throw new InvalidOperationException("No lossless export representation is available.");
        // Seed a valid upper bound; all alternatives remain in the exact search.
        for (var i = 0; i < groups.Length; i++)
        {
            var options = Options(i).OrderBy(o => o.Added).ThenBy(o => SwitchCost(i, o.Bank, o.Choice)).ToArray();
            if (options.Length == 0) break;
            Apply(options[0]); selected[i] = (options[0].Bank, options[0].Choice);
            if (i == groups.Length - 1) Save();
        }
        banks.Clear();
        Search(0, 0, 0);
        if (best is not null) return best;
        if (!hasLayout) throw new InvalidOperationException(IndexRangeError);
        throw new InvalidDataException("No lossless export representation fits this arrangement.");

        static string ChoiceKey(OHsExportRepresentation c) => c.NativeVolume + ":" + string.Join('|', c.Slots.OrderBy(p => p.Key.Sampleset).ThenBy(p => p.Key.Type).Select(p => p.Key + ":" + p.Value.Id));
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
                    token.ThrowIfCancellationRequested();
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
            if (accept is not null && !accept(plan)) return;
            var distance = selected.Select((s, i) => Math.Abs(s.Choice.NativeVolume - (preferredVolumes?[i] ?? 100))).Sum();
            var key = distance.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + ":" + BanksKey() + ":" + string.Join('|', selected.Select(s => s.Bank + ":" + ChoiceKey(s.Choice)));
            if (best is null || plan.AudioFileCount < best.AudioFileCount || plan.AudioFileCount == best.AudioFileCount &&
                (plan.AdditionalPointCount < best.AdditionalPointCount || plan.AdditionalPointCount == best.AdditionalPointCount && string.CompareOrdinal(key, bestKey) < 0))
            { best = plan; bestKey = key; bestDistance = distance; progress?.Report(new(35, $"Optimizing sample reuse ({plan.AudioFileCount} files)")); }
        }
        void Search(int i, int files, int rows)
        {
            token.ThrowIfCancellationRequested();
            var bound = Bound(files);
            if (best is not null && (bound > best.AudioFileCount || bound == best.AudioFileCount && rows > best.AdditionalPointCount)) return;
            var distanceSoFar = selected.Take(i).Select((s, n) => Math.Abs(s.Choice.NativeVolume - (preferredVolumes?[n] ?? 100))).Sum();
            if (best is not null && bound == best.AudioFileCount && rows == best.AdditionalPointCount && distanceSoFar > bestDistance) return;
            if (i == groups.Length) { Save(); return; }
            var key = i + ":" + BanksKey() + ":" + (i == 0 ? "" : selected[0].Choice.NativeVolume + ":" + selected[i - 1].Bank + ":" + selected[i - 1].Choice.NativeVolume);
            var prefix = selected.Take(i).Select((s, n) => Math.Abs(s.Choice.NativeVolume - (preferredVolumes?[n] ?? 100))).Sum().ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + ":" + string.Join('|', selected.Take(i).Select(s => s.Bank + ":" + ChoiceKey(s.Choice)));
            if (accept is null && memo.TryGetValue(key, out var prior) && (prior.Rows < rows || prior.Rows == rows && string.CompareOrdinal(prior.Prefix, prefix) <= 0)) return;
            memo[key] = (rows, prefix);
            foreach (var option in Options(i).OrderBy(o => o.Added).ThenBy(o => SwitchCost(i, o.Bank, o.Choice)).ToArray())
            {
                var nextRows = rows + SwitchCost(i, option.Bank, option.Choice);
                if (best is not null && (files + option.Added > best.AudioFileCount || files + option.Added == best.AudioFileCount && nextRows > best.AdditionalPointCount)) continue;
                var isNew = option.Bank == banks.Count;
                var previous = isNew ? null : new Dictionary<OHsExportSlot, OHsExportAsset>(banks[option.Bank]);
                Apply(option); selected[i] = (option.Bank, option.Choice);
                Search(i + 1, files + option.Added, nextRows);
                if (isNew) banks.RemoveAt(banks.Count - 1); else banks[option.Bank] = previous!;
            }
        }
    }
}
