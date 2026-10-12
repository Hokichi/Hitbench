using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Services;

public static class InstrumentalPlaybackEvents
{
    public static IReadOnlyList<SamplePlaybackEvent> Build(IReadOnlyDictionary<string, string> keyFiles,
        IReadOnlyList<InstrumentalNoteSnapshot> notes, int trackVolume, IReadOnlyCollection<string>? availableFiles, Func<Guid, long> resolveId)
    {
        var events = new List<SamplePlaybackEvent>();
        foreach (var note in notes)
        {
            if (!keyFiles.TryGetValue(note.Key, out var name)) continue;
            if (availableFiles is not null)
            { var found = availableFiles.FirstOrDefault(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase)); if (found is null) continue; name = found; }
            events.Add(new(resolveId(note.Id), name, note.Timestamp, note.Volume, trackVolume));
        }
        return events;
    }
}
