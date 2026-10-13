using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services;

public sealed class NodeHistoryManager
{
    private readonly Stack<IReadOnlyList<NodeHistoryEntry>> undo = new();
    private readonly Stack<IReadOnlyList<NodeHistoryEntry>> redo = new();

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public void RecordPlacement(Track track, Sample sample, int index) =>
        RecordBatch([new(track, sample, index, NodeHistoryOperation.Placement)]);

    public void RecordRemoval(Track track, Sample sample, int index) =>
        RecordBatch([new(track, sample, index, NodeHistoryOperation.Removal)]);

    public void RecordBatch(IReadOnlyList<NodeHistoryEntry> entries)
    {
        var batch = entries.Where(entry => entry.Operation != NodeHistoryOperation.Movement ||
            entry.PreviousTimestamp != entry.NextTimestamp || entry.DestinationTrack is not null).ToArray();
        if (batch.Length == 0) return;
        undo.Push(batch);
        redo.Clear();
    }

    public bool Undo(IReadOnlyCollection<Track> tracks) => Replay(undo, redo, tracks, true);
    public bool Redo(IReadOnlyCollection<Track> tracks) => Replay(redo, undo, tracks, false);

    private static bool Replay(Stack<IReadOnlyList<NodeHistoryEntry>> source, Stack<IReadOnlyList<NodeHistoryEntry>> destination,
        IReadOnlyCollection<Track> tracks, bool reverse)
    {
        if (!source.TryPeek(out var batch)) return false;
        var entries = reverse ? batch.Reverse().ToArray() : batch.ToArray();
        var shadows = new Dictionary<Track, List<Sample>>();
        var timestamps = new Dictionary<Sample, int>();
        foreach (var entry in entries)
        {
            if (!tracks.Any(track => ReferenceEquals(track, entry.Track))) return false;
            if (!shadows.TryGetValue(entry.Track, out var list)) shadows[entry.Track] = list = [.. entry.Track.Samples];
            if (entry.DestinationTrack is { } target)
            {
                if (!tracks.Contains(target)) return false;
                if (!shadows.TryGetValue(target, out var targetList)) shadows[target] = targetList = [.. target.Samples];
                if (!ApplyTransfer(entry, list, targetList, reverse, timestamps)) return false;
            }
            else if (!Apply(entry, list, reverse, timestamps)) return false;
        }
        foreach (var entry in entries)
            if (entry.DestinationTrack is { } target) ApplyTransfer(entry, entry.Track.Samples, target.Samples, reverse, null);
            else Apply(entry, entry.Track.Samples, reverse, null);
        source.Pop();
        destination.Push(batch);
        return true;
    }

    private static bool ApplyTransfer(NodeHistoryEntry entry, List<Sample> source, List<Sample> destination,
        bool reverse, Dictionary<Sample, int>? preview)
    {
        var from = reverse ? destination : source;
        var to = reverse ? source : destination;
        var position = reverse ? entry.Index : entry.DestinationIndex;
        var index = from.IndexOf(entry.Sample);
        var current = preview is not null && preview.TryGetValue(entry.Sample, out var time) ? time : entry.Sample.Timestamp;
        if (index < 0 || to.Contains(entry.Sample) || position < 0 || position > to.Count ||
            current != (reverse ? entry.NextTimestamp : entry.PreviousTimestamp)) return false;
        from.RemoveAt(index); to.Insert(position, entry.Sample);
        var next = reverse ? entry.PreviousTimestamp : entry.NextTimestamp;
        if (preview is not null) preview[entry.Sample] = next;
        else { entry.Sample.Timestamp = next; entry.Sample.FileName = (reverse ? entry.PreviousFileName : entry.NextFileName) ?? entry.Sample.FileName; }
        return true;
    }

    private static bool Apply(NodeHistoryEntry entry, List<Sample> samples, bool reverse,
        Dictionary<Sample, int>? preview)
    {
        var index = samples.FindIndex(sample => ReferenceEquals(sample, entry.Sample));
        if (entry.Operation == NodeHistoryOperation.Movement)
        {
            var current = preview is not null && preview.TryGetValue(entry.Sample, out var time) ? time : entry.Sample.Timestamp;
            var expected = reverse ? entry.NextTimestamp : entry.PreviousTimestamp;
            if (index < 0 || current != expected) return false;
            var next = reverse ? entry.PreviousTimestamp : entry.NextTimestamp;
            if (preview is not null) preview[entry.Sample] = next;
            else entry.Sample.Timestamp = next;
            return true;
        }
        if (entry.Operation is not (NodeHistoryOperation.Placement or NodeHistoryOperation.Removal)) return false;
        var insert = (entry.Operation == NodeHistoryOperation.Removal) == reverse;
        if (insert)
        {
            if (index >= 0 || entry.Index < 0 || entry.Index > samples.Count) return false;
            if (preview is null) entry.Sample.FileName =
                (entry.Operation == NodeHistoryOperation.Placement ? entry.NextFileName : null) ??
                entry.Track.SelectedSampleFileName ?? entry.Sample.FileName;
            samples.Insert(entry.Index, entry.Sample);
        }
        else
        {
            if (index < 0) return false;
            samples.RemoveAt(index);
        }
        return true;
    }

    public void RemoveTrack(Track track)
    {
        Prune(undo, track);
        Prune(redo, track);
    }

    private static void Prune(Stack<IReadOnlyList<NodeHistoryEntry>> stack, Track track)
    {
        var retained = stack.Select(batch => batch.Any(entry => entry.DestinationTrack is not null && (ReferenceEquals(entry.Track, track) || ReferenceEquals(entry.DestinationTrack, track))) ? [] : batch.Where(entry => !ReferenceEquals(entry.Track, track)).ToArray())
            .Where(batch => batch.Length > 0).Reverse().ToArray();
        stack.Clear();
        foreach (var entry in retained) stack.Push(entry);
    }

    public void Clear()
    {
        undo.Clear();
        redo.Clear();
    }

    public void RenameSample(string oldFileName, string newFileName)
    {
        foreach (var stack in new[] { undo, redo })
        {
            var batches = stack.Reverse().Select(batch => batch.Select(entry =>
            {
                if (string.Equals(entry.Sample.FileName, oldFileName, StringComparison.OrdinalIgnoreCase)) entry.Sample.FileName = newFileName;
                return entry with
                {
                    PreviousFileName = string.Equals(entry.PreviousFileName, oldFileName, StringComparison.OrdinalIgnoreCase) ? newFileName : entry.PreviousFileName,
                    NextFileName = string.Equals(entry.NextFileName, oldFileName, StringComparison.OrdinalIgnoreCase) ? newFileName : entry.NextFileName
                };
            }).ToArray()).ToArray();
            stack.Clear(); foreach (var batch in batches) stack.Push(batch);
        }
    }

    public void RenameSampleFolder(string oldFolderName, string newFolderName)
    {
        var prefix = oldFolderName + "/";
        var names = undo.Concat(redo).SelectMany(batch => batch)
            .SelectMany(entry => new[] { entry.Sample.FileName, entry.PreviousFileName, entry.NextFileName })
            .OfType<string>().Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var name in names) RenameSample(name, newFolderName + "/" + name[prefix.Length..]);
    }
}
