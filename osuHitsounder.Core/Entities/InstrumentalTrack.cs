using osuHitsounder.Core.Constants;

namespace osuHitsounder.Core.Entities;

public sealed class InstrumentalTrack : Track
{
    public Dictionary<string,string> KeyFiles { get; set; } = new(StringComparer.Ordinal);
    public List<InstrumentalNote> Notes { get; set; } = [];
    public InstrumentalTrack() { Sampleset="Soft"; Addition="None"; SampleType=Enums.SampleType.Whistle; }
    public bool TryAddNote(InstrumentalNote note)
    {
        if (!Valid(note.Key,note.Timestamp) || note.Volume is <0 or >100 || Notes.Contains(note) || Collides(note.Key,note.Timestamp,null)) return false;
        Notes.Add(note); return true;
    }
    public bool TryMoveNote(InstrumentalNote note,string key,int timestamp)
    {
        if (!Notes.Contains(note) || !Valid(key,timestamp) || Collides(key,timestamp,note)) return false;
        note.Key=key; note.Timestamp=timestamp; return true;
    }
    public bool TryRemoveNote(InstrumentalNote note) => Notes.Remove(note);
    private static bool Valid(string key,int time) => time>=0 && InstrumentalKeys.TryGetIndex(key,out _);
    private bool Collides(string key,int time,InstrumentalNote? except) => Notes.Any(n=>n!=except && n.Key==key && n.Timestamp==time);
}
