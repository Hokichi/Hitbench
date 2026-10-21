using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Services;

public static class InstrumentalPlaybackEvents
{
    public static IReadOnlyList<SamplePlaybackEvent> Build(IReadOnlyDictionary<string, string> keyFiles,
        IReadOnlyList<InstrumentalNoteSnapshot> notes, int trackVolume, IReadOnlyCollection<string>? availableFiles, Func<Guid, long> resolveId)
        => BuildWithLookup(keyFiles, notes, trackVolume,
            availableFiles?.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(f => f, f => f, StringComparer.OrdinalIgnoreCase), resolveId);

    internal static IReadOnlyList<SamplePlaybackEvent> BuildWithLookup(IReadOnlyDictionary<string, string> keyFiles,
        IReadOnlyList<InstrumentalNoteSnapshot> notes, int trackVolume, IReadOnlyDictionary<string, string>? availableFiles, Func<Guid, long> resolveId)
    {
        var events = new List<SamplePlaybackEvent>();
        foreach (var note in notes)
        {
            if (!keyFiles.TryGetValue(note.Key, out var name)) continue;
            if (availableFiles is not null)
            { if (!availableFiles.TryGetValue(name, out var found)) continue; name = found; }
            events.Add(new(resolveId(note.Id), name, note.Timestamp, trackVolume));
        }
        return events;
    }
}
