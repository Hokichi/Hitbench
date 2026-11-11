using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
namespace Hitbench.Services;

public static class InstrumentalPlaybackEvents
{
    public static IReadOnlyList<SamplePlaybackEvent> Build(IReadOnlyDictionary<string, string> keyFiles,
        IReadOnlyList<InstrumentalNoteSnapshot> notes, int trackVolume, IReadOnlyCollection<string>? availableFiles, Func<Guid, long> resolveId,
        double durationMilliseconds = double.PositiveInfinity)
        => BuildWithLookup(keyFiles, notes, trackVolume,
            availableFiles?.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(f => f, f => f, StringComparer.OrdinalIgnoreCase), resolveId, durationMilliseconds);

    internal static IReadOnlyList<SamplePlaybackEvent> BuildWithLookup(IReadOnlyDictionary<string, string> keyFiles,
        IReadOnlyList<InstrumentalNoteSnapshot> notes, int trackVolume, IReadOnlyDictionary<string, string>? availableFiles, Func<Guid, long> resolveId,
        double durationMilliseconds = double.PositiveInfinity)
    {
        var events = new List<SamplePlaybackEvent>();
        foreach (var note in notes)
        {
            if (note.IsReadOnly || note.Timestamp < 0 || note.Timestamp >= durationMilliseconds) continue;
            if (!keyFiles.TryGetValue(note.Key, out var name)) continue;
            if (availableFiles is not null)
            { if (!availableFiles.TryGetValue(name, out var found)) continue; name = found; }
            events.Add(new(resolveId(note.Id), name, note.Timestamp, trackVolume));
        }
        return events;
    }
}
