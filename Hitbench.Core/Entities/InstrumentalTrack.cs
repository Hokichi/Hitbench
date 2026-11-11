using Hitbench.Core.Constants;

namespace Hitbench.Core.Entities;

public sealed class InstrumentalTrack : Track
{
    public string FolderName { get; set; } = "";
    public Dictionary<string, string> KeyFiles { get; set; } = new(StringComparer.Ordinal);
    public List<InstrumentalNote> Notes { get; set; } = [];
    public InstrumentalTrack() { Addition = "Soft"; SampleType = Enums.SampleType.Whistle; }
    public bool TryAddNote(InstrumentalNote note)
    {
        if (note.IsReadOnly || !Valid(note.Key, note.Timestamp) || Notes.Contains(note) || Collides(note.Key, note.Timestamp, null)) return false;
        Notes.Add(note); return true;
    }
    public bool TryMoveNote(InstrumentalNote note, string key, int timestamp)
    {
        if (note.IsReadOnly || !Notes.Contains(note) || !Valid(key, timestamp) || Collides(key, timestamp, note)) return false;
        note.Key = key; note.Timestamp = timestamp; return true;
    }
    public bool TryRemoveNote(InstrumentalNote note) => !note.IsReadOnly && Notes.Remove(note);
    public bool TryReplaceNotes(IReadOnlyList<InstrumentalNote> notes)
    {
        var replacement = notes.ToArray();
        var locked = Notes.Where(n => n.IsReadOnly).Select(n => (n.Key, n.Timestamp)).ToHashSet();
        if (!locked.SetEquals(replacement.Where(n => n.IsReadOnly).Select(n => (n.Key, n.Timestamp))) ||
            replacement.Any(n => !InstrumentalKeys.TryGetIndex(n.Key, out _) || n.Timestamp < 0 && !n.IsReadOnly) ||
            replacement.Distinct().Count() != replacement.Length ||
            replacement.Select(n => (n.Key, n.Timestamp)).Distinct().Count() != replacement.Length) return false;
        Notes.Clear(); Notes.AddRange(replacement); return true;
    }
    private static bool Valid(string key, int time) => time >= 0 && InstrumentalKeys.TryGetIndex(key, out _);
    private bool Collides(string key, int time, InstrumentalNote? except) => Notes.Any(n => n != except && n.Key == key && n.Timestamp == time);
}
