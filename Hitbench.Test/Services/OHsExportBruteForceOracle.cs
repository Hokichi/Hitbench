using Hitbench.Resources.DataModels;
namespace Hitbench.Test.Services;

public static class OHsExportBruteForceOracle
{
    public static int? MinimumAudioFiles(IReadOnlyList<IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, string>>> recipes, int capacity, IReadOnlySet<string>? zeroFileAssetIds = null)
    {
        int? minimum = null;
        var assignment = new int[recipes.Count];
        var selected = new IReadOnlyDictionary<OHsExportSlot, string>[recipes.Count];
        Enumerate(0);
        return minimum;
        void Enumerate(int next)
        {
            if (next == recipes.Count)
            {
                var banks = new Dictionary<int, Dictionary<OHsExportSlot, string>>();
                for (var i = 0; i < recipes.Count; i++)
                {
                    if (!banks.TryGetValue(assignment[i], out var bank)) banks[assignment[i]] = bank = new();
                    foreach (var pair in selected[i])
                    {
                        if (bank.TryGetValue(pair.Key, out var id) && id != pair.Value) return;
                        bank[pair.Key] = pair.Value;
                    }
                }
                var files = banks.Values.Sum(b => b.Values.Count(id => zeroFileAssetIds?.Contains(id) != true));
                if (minimum is null || files < minimum) minimum = files;
                return;
            }
            for (var bank = 0; bank < capacity; bank++)
                foreach (var choice in recipes[next])
                { assignment[next] = bank; selected[next] = choice; Enumerate(next + 1); }
        }
    }
}
