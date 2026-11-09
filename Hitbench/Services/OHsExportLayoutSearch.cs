using System.Collections.ObjectModel;
using System.Diagnostics;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;

namespace Hitbench.Services;

internal sealed class OHsExportLayoutSearch
{
    private const string IndexRangeError = "The selected custom sample index range cannot represent this arrangement.";
    private static readonly string[] Sets = ["Normal", "Soft", "Drum"];
    private readonly int[][][] recipes;
    private readonly OHsExportAsset[] assets;
    private readonly int capacity;
    private readonly CancellationToken token;
    private readonly IProgress<OHsExportProgress>? progress;
    private readonly List<int[]> banks = [];
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Slot, int Asset), int> copies = [];
    private readonly HashSet<int> flexible = [];
    private readonly int minimumBanks;
    private readonly int minimumSlots;
    private int[][]? best;
    private int bestFiles = int.MaxValue;
    private long lastReport = Stopwatch.GetTimestamp();

    private OHsExportLayoutSearch(IReadOnlyList<IReadOnlyList<OHsExportRepresentation>> input, int capacity,
        CancellationToken token, IProgress<OHsExportProgress>? progress)
    {
        this.capacity = capacity; this.token = token; this.progress = progress;
        assets = input.SelectMany(r => { Check(); return r; }).SelectMany(c => c.Slots.Values).DistinctBy(a => a.Id)
            .OrderBy(a => a.Id, StringComparer.Ordinal).ToArray();
        var ids = assets.Select((a, i) => (a.Id, Index: i + 1)).ToDictionary(p => p.Id, p => p.Index, StringComparer.Ordinal);
        var distinct = new SortedDictionary<string, int[][]>(StringComparer.Ordinal);
        foreach (var recipe in input)
        {
            Check();
            var choices = recipe.Select(c =>
            {
                Check();
                var map = new int[12];
                foreach (var pair in c.Slots) map[Array.IndexOf(Sets, pair.Key.Sampleset) * 4 + (int)pair.Key.Type] = ids[pair.Value.Id];
                return map;
            }).DistinctBy(MapKey).OrderBy(MapKey, StringComparer.Ordinal).ToArray();
            distinct.TryAdd(string.Join('|', choices.Select(MapKey)), choices);
        }
        recipes = distinct.Values.ToArray();
        minimumSlots = recipes.Select(r => r.Min(Count)).DefaultIfEmpty().Max();
        var forced = new List<int[]>();
        foreach (var recipe in recipes)
        {
            Check();
            var common = (int[])recipe[0].Clone();
            var required = recipe[0].Where(id => id != 0).ToHashSet();
            foreach (var choice in recipe.Skip(1))
            {
                Check();
                for (var s = 0; s < 12; s++) if (common[s] != choice[s]) common[s] = 0;
                required.IntersectWith(choice.Where(id => id != 0));
            }
            forced.Add(common);
            flexible.UnionWith(required);
        }
        var pairs = forced.SelectMany(m => m.Select((id, slot) => (Slot: slot, Asset: id)))
            .Where(p => p.Asset != 0).Distinct().ToArray();
        if (pairs.GroupBy(p => p.Slot).Any(g => g.Count() > capacity)) throw new InvalidOperationException(IndexRangeError);
        foreach (var pair in pairs)
        {
            Check();
            var together = forced.Where(m => m[pair.Slot] == pair.Asset).ToArray();
            var required = 1;
            // Different mandatory identities in another slot cannot share a bank,
            // so this identity needs at least that many physical copies too.
            for (var s = 0; s < 12; s++)
                required = Math.Max(required, together.Select(m => m[s]).Where(id => id != 0).Distinct().Count());
            copies[pair] = required;
        }
        flexible.ExceptWith(copies.Keys.Select(p => p.Asset));
        minimumBanks = copies.GroupBy(p => p.Key.Slot).Select(g => g.Sum(p => p.Value)).DefaultIfEmpty(recipes.Length == 0 ? 0 : 1).Max();
    }

    internal static IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>> Find(
        IReadOnlyList<IReadOnlyList<OHsExportRepresentation>> recipes, int capacity, CancellationToken token,
        IProgress<OHsExportProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        if (capacity is < 1 or > 999) throw new ArgumentOutOfRangeException(nameof(capacity));
        progress?.Report(new(35, "Optimizing sample reuse"));
        token.ThrowIfCancellationRequested();
        var search = new OHsExportLayoutSearch(recipes, capacity, token, progress);
        if (search.minimumBanks > capacity) throw new InvalidOperationException(IndexRangeError);
        search.Seed();
        search.Search();
        token.ThrowIfCancellationRequested();
        var result = search.Snapshot();
        token.ThrowIfCancellationRequested();
        return result;
    }

    private bool IsFile(int id) => id != 0 && !assets[id - 1].IsNativeFallback;
    private int Count(int[] map) => map.Count(IsFile);
    private static string MapKey(int[] map) => string.Join(',', map);

    private static bool Contains(int[] bank, int[] map)
    {
        for (var s = 0; s < 12; s++) if (map[s] != 0 && bank[s] != map[s]) return false;
        return true;
    }

    private int Added(int[] bank, int[] map)
    {
        var added = 0;
        for (var s = 0; s < 12; s++)
        {
            if (map[s] != 0 && bank[s] != 0 && map[s] != bank[s]) return -1;
            if (IsFile(map[s]) && bank[s] == 0) added++;
        }
        return added;
    }

    private void Check()
    {
        token.ThrowIfCancellationRequested();
        if (progress is not null && Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds >= 250) Report();
    }

    private void Report()
    {
        progress?.Report(new(35, best is null ? "Optimizing sample reuse" : $"Optimizing sample reuse ({bestFiles} files)"));
        lastReport = Stopwatch.GetTimestamp();
        token.ThrowIfCancellationRequested();
    }

    private int Bound(int files)
    {
        var missing = 0;
        foreach (var pair in copies)
        {
            Check();
            if (IsFile(pair.Key.Asset)) missing += Math.Max(0, pair.Value - banks.Count(b => b[pair.Key.Slot] == pair.Key.Asset));
        }
        return Math.Max(minimumSlots, files + missing + flexible.Count(id => IsFile(id) && !banks.Any(b => b.Contains(id))));
    }

    private List<(int Bank, int[] Map, int Added)> Options(out bool complete)
    {
        complete = true;
        List<(int Bank, int[] Map, int Added)>? selected = null;
        var selectedSize = -1;
        foreach (var recipe in recipes)
        {
            Check();
            if (recipe.Any(m => banks.Any(b => Contains(b, m)))) continue;
            complete = false;
            var options = new List<(int Bank, int[] Map, int Added)>();
            foreach (var map in recipe)
            {
                Check();
                for (var b = 0; b < banks.Count; b++)
                {
                    Check();
                    var added = Added(banks[b], map);
                    if (added >= 0) options.Add((b, map, added));
                }
                if (banks.Count < capacity) options.Add((banks.Count, map, Count(map)));
            }
            var size = recipe.Min(Count);
            if (selected is null || options.Count < selected.Count || options.Count == selected.Count && size > selectedSize)
            { selected = options; selectedSize = size; }
        }
        return (selected ?? []).OrderBy(o => o.Added).ThenBy(o => o.Bank).ToList();
    }

    private int[]? Apply((int Bank, int[] Map, int Added) option)
    {
        var previous = option.Bank == banks.Count ? null : banks[option.Bank];
        var map = previous is null ? new int[12] : (int[])previous.Clone();
        for (var s = 0; s < 12; s++) if (option.Map[s] != 0) map[s] = option.Map[s];
        if (previous is null) banks.Add(map); else banks[option.Bank] = map;
        return previous;
    }

    private void Undo(int index, int[]? previous)
    { if (previous is null) banks.RemoveAt(index); else banks[index] = previous; }

    private void Save(int files)
    {
        bestFiles = files;
        best = banks.Select(b => (int[])b.Clone()).OrderBy(MapKey, StringComparer.Ordinal).ToArray();
        Report();
    }

    private void Seed()
    {
        var files = 0;
        while (true)
        {
            Check();
            var options = Options(out var complete);
            if (complete) { Save(files); break; }
            if (options.Count == 0) break;
            Apply(options[0]);
            files += options[0].Added;
        }
        banks.Clear();
    }

    private bool Visit(int files, out List<(int Bank, int[] Map, int Added)> options)
    {
        Check();
        options = [];
        if (files >= bestFiles || Bound(files) >= bestFiles) return false;
        if (!seen.Add(string.Join(';', banks.Select(MapKey).Order(StringComparer.Ordinal)))) return false;
        options = Options(out var complete);
        if (!complete) return true;
        Save(files);
        return false;
    }

    private void Search()
    {
        if (!Visit(0, out var options)) return;
        var stack = new Stack<(List<(int Bank, int[] Map, int Added)> Options, int Next, int Files, int Bank, int[]? Previous)>();
        stack.Push((options, 0, 0, -1, null));
        while (stack.TryPop(out var frame))
        {
            Check();
            if (frame.Next == frame.Options.Count)
            {
                if (frame.Bank >= 0) Undo(frame.Bank, frame.Previous);
                continue;
            }
            stack.Push((frame.Options, frame.Next + 1, frame.Files, frame.Bank, frame.Previous));
            var option = frame.Options[frame.Next];
            var files = frame.Files + option.Added;
            if (files >= bestFiles) continue;
            var previous = Apply(option);
            if (Visit(files, out var next)) stack.Push((next, 0, files, option.Bank, previous));
            else Undo(option.Bank, previous);
        }
    }

    private IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>> Snapshot()
    {
        if (best is null) throw new InvalidOperationException(IndexRangeError);
        return Array.AsReadOnly(best.Select(map =>
        {
            var slots = new Dictionary<OHsExportSlot, OHsExportAsset>();
            for (var s = 0; s < 12; s++)
                if (map[s] != 0) slots.Add(new(Sets[s / 4], (SampleType)(s % 4)), assets[map[s] - 1]);
            return (IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>)new ReadOnlyDictionary<OHsExportSlot, OHsExportAsset>(slots);
        }).ToArray());
    }
}
