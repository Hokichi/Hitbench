using System.Collections.ObjectModel;
using osuHitsounder.Core.Enums;
using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Services;

public static class OHsExportRepresentations
{
    private static readonly string[] Sets = ["Normal", "Soft", "Drum"];
    public static string FileName(OHsExportSlot slot, int index, string extension)
    {
        if (!Sets.Contains(slot.Sampleset) || !Enum.IsDefined(slot.Type) || index is < 1 or > 999 || extension is not (".ogg" or ".wav" or ".mp3")) throw new ArgumentException("Invalid export slot filename.");
        return $"{slot.Sampleset.ToLowerInvariant()}-hit{slot.Type.ToString().ToLowerInvariant()}{(index == 1 ? "" : index.ToString(System.Globalization.CultureInfo.InvariantCulture))}{extension}";
    }
    public static IReadOnlyList<OHsExportRepresentation> Build(IReadOnlyList<OHsExportVoice> voices, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var components = voices.GroupBy(v => (v.Sampleset, v.Type))
            .SelectMany(g => g.GroupBy(v => v.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(f => (Slot: new OHsExportSlot(g.Key.Sampleset, g.Key.Type), Source: (FileName: f.Key, GainPercent: checked(f.Sum(v => v.GainPercent)))))).ToArray();
        var results = new Dictionary<string, OHsExportRepresentation>(StringComparer.Ordinal);
        var bestParticipants = int.MaxValue;
        var groups = new List<List<int>>();
        var silence = OHsExportAsset.Create([]);
        for (var count = Math.Min(4, components.Length); count >= 1; count--)
        {
            Partition(0, count);
            if (results.Count > 0) break;
        }
        if (components.Length == 0)
            foreach (var set in Sets) results.Add(set, new(set, "Normal", 0, new Dictionary<OHsExportSlot, OHsExportAsset> { [new(set, SampleType.Normal)] = silence }, 0, 0));
        return Array.AsReadOnly(results.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value).ToArray());

        void Partition(int next, int count)
        {
            token.ThrowIfCancellationRequested();
            if (groups.Count > count || groups.Count + components.Length - next < count) return;
            if (next == components.Length)
            {
                if (groups.Count != count) return;
                var participants = groups.Where(g => g.Count > 1).Sum(g => g.Count);
                if (participants > bestParticipants) return;
                Assign(0, new Dictionary<OHsExportSlot, OHsExportAsset>(), participants);
                return;
            }
            foreach (var group in groups.ToArray())
            {
                // Normal and additional sounds have independently selectable families.
                if ((components[group[0]].Slot.Type == SampleType.Normal) != (components[next].Slot.Type == SampleType.Normal)) continue;
                group.Add(next); Partition(next + 1, count); group.RemoveAt(group.Count - 1);
            }
            if (groups.Count < count) { groups.Add([next]); Partition(next + 1, count); groups.RemoveAt(groups.Count - 1); }
        }
        void Assign(int next, Dictionary<OHsExportSlot, OHsExportAsset> slots, int participants)
        {
            token.ThrowIfCancellationRequested();
            if (next < groups.Count)
            {
                var group = groups[next];
                foreach (var slot in group.Select(i => components[i].Slot).Distinct())
                {
                    if (slots.ContainsKey(slot) || slot.Type == SampleType.Normal && slots.Keys.Any(k => k.Type == SampleType.Normal) ||
                        slot.Type != SampleType.Normal && slots.Keys.Any(k => k.Type != SampleType.Normal && k.Sampleset != slot.Sampleset)) continue;
                    slots.Add(slot, OHsExportAsset.Create(group.Select(i => components[i].Source)));
                    Assign(next + 1, slots, participants); slots.Remove(slot);
                }
                return;
            }
            var normal = slots.Keys.FirstOrDefault(s => s.Type == SampleType.Normal);
            var addition = slots.Keys.FirstOrDefault(s => s.Type != SampleType.Normal)?.Sampleset ?? "Normal";
            var flags = slots.Keys.Aggregate(0, (mask, slot) => mask | (slot.Type switch { SampleType.Normal => 1, SampleType.Whistle => 2, SampleType.Finish => 4, SampleType.Clap => 8, _ => throw new ArgumentException("Invalid sample type.") }));
            foreach (var normalSet in normal is null ? Sets : [normal.Sampleset])
            {
                var map = slots.ToDictionary();
                if (normal is null) map.Add(new(normalSet, SampleType.Normal), silence);
                var key = string.Join("|", map.OrderBy(p => p.Key.Sampleset).ThenBy(p => p.Key.Type).Select(p => $"{p.Key}:{p.Value.Id}"));
                if (participants < bestParticipants) { results.Clear(); bestParticipants = participants; }
                results.TryAdd(key, new(normalSet, addition, flags, map, components.Length - groups.Count, participants));
            }
        }
    }
}
