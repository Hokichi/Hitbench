using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services;

public sealed class NodeHistoryManager
{
    private readonly Stack<NodeHistoryEntry> undo = new();
    private readonly Stack<NodeHistoryEntry> redo = new();

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public void RecordPlacement(Track track, Sample sample, int index) =>
        Record(new(track, sample, index, NodeHistoryOperation.Placement));

    public void RecordRemoval(Track track, Sample sample, int index) =>
        Record(new(track, sample, index, NodeHistoryOperation.Removal));

    private void Record(NodeHistoryEntry entry)
    {
        undo.Push(entry);
        redo.Clear();
    }

    public bool Undo(IReadOnlyCollection<Track> tracks) => Replay(undo, redo, tracks, true);
    public bool Redo(IReadOnlyCollection<Track> tracks) => Replay(redo, undo, tracks, false);

    private static bool Replay(Stack<NodeHistoryEntry> source, Stack<NodeHistoryEntry> destination,
        IReadOnlyCollection<Track> tracks, bool reverse)
    {
        if (!source.TryPeek(out var entry) || !tracks.Any(track => ReferenceEquals(track, entry.Track)))
            return false;
        var samples = entry.Track.Samples;
        var index = samples.FindIndex(sample => ReferenceEquals(sample, entry.Sample));
        var insert = (entry.Operation == NodeHistoryOperation.Removal) == reverse;
        if (insert)
        {
            if (index >= 0 || entry.Index < 0 || entry.Index > samples.Count) return false;
            entry.Sample.FileName = entry.Track.SelectedSampleFileName ?? entry.Sample.FileName;
            samples.Insert(entry.Index, entry.Sample);
        }
        else
        {
            if (index < 0) return false;
            samples.RemoveAt(index);
        }
        source.Pop();
        destination.Push(entry);
        return true;
    }

    public void RemoveTrack(Track track)
    {
        Prune(undo, track);
        Prune(redo, track);
    }

    private static void Prune(Stack<NodeHistoryEntry> stack, Track track)
    {
        var retained = stack.Where(entry => !ReferenceEquals(entry.Track, track)).Reverse().ToArray();
        stack.Clear();
        foreach (var entry in retained) stack.Push(entry);
    }

    public void Clear()
    {
        undo.Clear();
        redo.Clear();
    }
}
