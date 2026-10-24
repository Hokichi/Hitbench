using Hitbench.Resources.DataModels;
namespace Hitbench.Test.Services;

public static class OHsExportBruteForceOracle
{
    public static (int AudioFiles, int AdditionalPoints)? MinimumCost(IReadOnlyList<(int Timestamp, IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, string>> Choices)> groups, IReadOnlyList<decimal> opportunities, int capacity)
    {
        (int AudioFiles, int AdditionalPoints)? minimum = null;
        var assignment = new int[groups.Count];
        var selected = new IReadOnlyDictionary<OHsExportSlot, string>[groups.Count];
        Enumerate(0);
        return minimum;
        void Enumerate(int next)
        {
            if (next == groups.Count)
            {
                var banks = new Dictionary<int, Dictionary<OHsExportSlot, string>>();
                for (var i = 0; i < groups.Count; i++)
                {
                    if (!banks.TryGetValue(assignment[i], out var bank)) banks[assignment[i]] = bank = new();
                    foreach (var pair in selected[i])
                    {
                        if (bank.TryGetValue(pair.Key, out var id) && id != pair.Value) return;
                        bank[pair.Key] = pair.Value;
                    }
                }
                var cost = 0;
                for (var i = 1; i < groups.Count; i++)
                    if (assignment[i] != assignment[i - 1] && !opportunities.Any(t => t > (decimal)groups[i - 1].Timestamp + 5 && t <= (decimal)groups[i].Timestamp + 5)) cost++;
                var candidate = (banks.Values.Sum(b => b.Count), cost);
                if (minimum is null || candidate.CompareTo(minimum.Value) < 0) minimum = candidate;
                return;
            }
            for (var bank = 0; bank < capacity; bank++)
                foreach (var choice in groups[next].Choices)
                { assignment[next] = bank; selected[next] = choice; Enumerate(next + 1); }
        }
    }
}
