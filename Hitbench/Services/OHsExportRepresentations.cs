using System.Collections.ObjectModel;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
namespace Hitbench.Services;

public static class OHsExportRepresentations
{
    private static readonly string[] Sets = ["Normal", "Soft", "Drum"];
    public static string FileName(OHsExportSlot slot, int index, string extension)
    {
        if (!Sets.Contains(slot.Sampleset) || !Enum.IsDefined(slot.Type) || index is < 1 or > 999 || extension is not (".ogg" or ".wav" or ".mp3")) throw new ArgumentException("Invalid export slot filename.");
        return $"{slot.Sampleset.ToLowerInvariant()}-hit{slot.Type.ToString().ToLowerInvariant()}{(index == 1 ? "" : index.ToString(System.Globalization.CultureInfo.InvariantCulture))}{extension}";
    }
    internal static IReadOnlyList<OHsExportRepresentation> Shape(IReadOnlyList<OHsExportRepresentation> raw,
        int timestamp, ControlPointVolumeTimeline timeline, Func<string, DecodedAudio> resolve,
        IDictionary<string, OHsExportAsset> assets, CancellationToken token)
    {
        var choices = new List<OHsExportRepresentation>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        var cache = new Dictionary<(string Id, int Native), OHsExportAsset?>();
        var linear = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var original in raw)
            for (var native = 100; native >= 0; native--)
            {
                token.ThrowIfCancellationRequested();
                var slots = new Dictionary<OHsExportSlot, OHsExportAsset>();
                foreach (var pair in original.Slots)
                {
                    if (!cache.TryGetValue((pair.Value.Id, native), out var shaped))
                    {
                        var sources = pair.Value.Sources.Select(s => new OHsExportSource(s.FileName, s.GainPercent,
                            OHsExportEnvelope.Create(timeline, ControlPointVolumeTimeline.ToFrame(timestamp), resolve(s.FileName).FrameCount))).ToArray();
                        if (!linear.ContainsKey(pair.Value.Id)) linear[pair.Value.Id] = Equivalent(sources, 99);
                        var silent = sources.All(s => s.GainPercent == 0 || s.Envelope!.Segments.All(e => e.StartNumerator.IsZero && e.SlopeNumerator.IsZero));
                        if (native == 0) shaped = silent ? OHsExportAsset.Create([], assets) : null;
                        else
                        {
                            var candidate = OHsExportAsset.CreateShaped(sources.Select(s => s with { Envelope = s.Envelope!.Scale(100, native) }), assets);
                            shaped = Equivalent(sources, native) ? candidate : null;
                        }
                        cache[(pair.Value.Id, native)] = shaped;
                    }
                    if (shaped is null) break;
                    slots.Add(pair.Key, shaped);
                }
                if (slots.Count != original.Slots.Count) continue;
                var key = native + ":" + string.Join('|', slots.OrderBy(p => p.Key.Sampleset).ThenBy(p => p.Key.Type).Select(p => p.Key + ":" + p.Value.Id));
                if (unique.Add(key)) choices.Add(new(original.NormalSet, original.AdditionSet, original.HitSound, slots, original.MergeCount, original.MixedComponentCount, native, original.Slots.Values.All(a => linear[a.Id])));
            }
        return choices.AsReadOnly();

        bool Equivalent(OHsExportSource[] sources, int native)
        {
            if (native == 100 || sources.Length == 0) return true;
            var audio = sources.Select(s => resolve(s.FileName)).ToArray();
            var count = audio.Max(a => a.Samples.Length);
            for (var i = 0; i < count; i++)
            {
                if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
                double sum = 0;
                for (var j = 0; j < sources.Length; j++)
                    if (i < audio[j].Samples.Length) sum += audio[j].Samples[i] * sources[j].GainPercent / 100d * sources[j].Envelope!.GetGain(i / 2);
                var reference = Math.Clamp(sum, -1, 1);
                var factored = Math.Clamp(sum * 100 / native, -1, 1) * native / 100;
                if (Math.Abs(reference - factored) > 1e-7) return false;
            }
            return true;
        }
    }
    public static IReadOnlyList<OHsExportRepresentation> Build(IReadOnlyList<OHsExportVoice> voices, CancellationToken token, IDictionary<string, OHsExportAsset>? assets = null)
    {
        token.ThrowIfCancellationRequested();
        var components = voices.GroupBy(v => (v.Sampleset, v.Type))
            .SelectMany(g => g.GroupBy(v => v.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(f => (Slot: new OHsExportSlot(g.Key.Sampleset, g.Key.Type), Source: (FileName: f.Key, GainPercent: checked(f.Sum(v => v.GainPercent)))))).ToArray();
        var results = new Dictionary<string, OHsExportRepresentation>(StringComparer.Ordinal);
        var bestParticipants = int.MaxValue;
        var groups = new List<List<int>>();
        var silence = OHsExportAsset.Create([], assets);
        var maximumGroups = (components.Any(c => c.Slot.Type == SampleType.Normal) ? 1 : 0) +
            components.Where(c => c.Slot.Type != SampleType.Normal).GroupBy(c => c.Slot.Sampleset)
                .Select(g => g.Select(c => c.Slot.Type).Distinct().Count()).DefaultIfEmpty(0).Max();
        for (var count = maximumGroups; count >= 1; count--)
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
            if (groups.Any(g => g.Count > 1))
            {
                var maximumSingletons = groups.Count(g => g.Count == 1) + count - groups.Count;
                if (components.Length - maximumSingletons > bestParticipants) return;
            }
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
                    slots.Add(slot, OHsExportAsset.Create(group.Select(i => components[i].Source), assets));
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
