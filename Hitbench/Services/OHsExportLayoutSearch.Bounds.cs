using Hitbench.Resources.DataModels;

namespace Hitbench.Services;

internal sealed partial class OHsExportLayoutSearch
{
    private static bool Compatible(IReadOnlyDictionary<OHsExportSlot, OHsExportAsset> a, IReadOnlyDictionary<OHsExportSlot, OHsExportAsset> b)
        => a.All(p => !b.TryGetValue(p.Key, out var other) || other.Id == p.Value.Id);

    private static List<(OHsExportSlot Slot, HashSet<string> Ids, int Count)> BuildTimelineRequirements(
        OHsExportRepresentation[][] choices, CancellationToken token)
    {
        var maps = choices.Distinct().Select(group =>
        {
            token.ThrowIfCancellationRequested();
            return group[0].Slots.Keys.Where(k => group.All(c => { token.ThrowIfCancellationRequested(); return c.Slots.ContainsKey(k); }))
                .ToDictionary(k => k, k => group.Select(c => c.Slots[k].Id).ToHashSet(StringComparer.Ordinal));
        }).ToArray();
        var result = new List<(OHsExportSlot Slot, HashSet<string> Ids, int Count)>();
        foreach (var slot in maps.SelectMany(m => m.Keys).Distinct())
        {
            var eligible = maps.Where(m => m.ContainsKey(slot)).ToArray();
            // A clique of incompatible recipes requires separate physical banks, including copies of shared additions.
            var clique = new List<Dictionary<OHsExportSlot, HashSet<string>>>();
            foreach (var map in eligible)
            {
                token.ThrowIfCancellationRequested();
                if (clique.All(other => { token.ThrowIfCancellationRequested(); return map.Any(p => other.TryGetValue(p.Key, out var ids) && !ids.Overlaps(p.Value)); })) clique.Add(map);
            }
            result.Add((slot, clique.SelectMany(m => m[slot]).ToHashSet(StringComparer.Ordinal), clique.Count));
            // Disjoint alternative sets also give a bound independent of other mandatory slots.
            var disjoint = new HashSet<string>(StringComparer.Ordinal);
            var count = 0;
            foreach (var map in eligible.OrderBy(m => m[slot].Count))
            {
                token.ThrowIfCancellationRequested();
                if (disjoint.Overlaps(map[slot])) continue;
                disjoint.UnionWith(map[slot]); count++;
            }
            result.Add((slot, disjoint, count));
        }
        return result;
    }
}
