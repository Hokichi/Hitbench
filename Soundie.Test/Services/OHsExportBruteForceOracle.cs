using Soundie.Resources.DataModels;
namespace Soundie.Test.Services;

public static class OHsExportBruteForceOracle
{
    public static int MinimumAdditionalPoints(IReadOnlyList<(int Timestamp, IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, string>> Choices)> groups, IReadOnlyList<decimal> opportunities, int capacity)
    {
        var minimum = int.MaxValue;
        var assignment = new int[groups.Count];
        var selected = new IReadOnlyDictionary<OHsExportSlot, string>[groups.Count];
        Enumerate(0);
        return minimum;
        void Enumerate(int next)
        {
            if (next == groups.Count)
            {
                for (var a = 0; a < groups.Count; a++)
                    for (var b = a + 1; b < groups.Count; b++)
                        if (assignment[a] == assignment[b] && selected[a].Any(p => selected[b].TryGetValue(p.Key, out var id) && id != p.Value)) return;
                var cost = 0;
                for (var i = 1; i < groups.Count; i++)
                    if (assignment[i] != assignment[i - 1] && !opportunities.Any(t => t > (decimal)groups[i - 1].Timestamp + 5 && t <= (decimal)groups[i].Timestamp + 5)) cost++;
                minimum = Math.Min(minimum, cost); return;
            }
            for (var bank = 0; bank < capacity; bank++)
                foreach (var choice in groups[next].Choices)
                { assignment[next] = bank; selected[next] = choice; Enumerate(next + 1); }
        }
    }
}
