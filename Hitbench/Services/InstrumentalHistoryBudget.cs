namespace Hitbench.Services;

public sealed class InstrumentalHistoryBudget
{
    private readonly LinkedList<(InstrumentalNoteHistoryEntry Entry, long Bytes, Action Evict)> retained = new();
    public long MaximumBytes { get; }
    public long RetainedBytes { get; private set; }
    public InstrumentalHistoryBudget(long maximumBytes = 64L * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        MaximumBytes = maximumBytes;
    }
    internal bool MakeRoom(long bytes)
    {
        if (bytes > MaximumBytes) return false;
        while (RetainedBytes > MaximumBytes - bytes) retained.First!.Value.Evict();
        return true;
    }
    internal void Add(InstrumentalNoteHistoryEntry entry, long bytes, Action evict)
    {
        retained.AddLast((entry, bytes, evict));
        RetainedBytes = checked(RetainedBytes + bytes);
    }
    internal void Remove(InstrumentalNoteHistoryEntry entry)
    {
        for (var node = retained.First; node is not null; node = node.Next)
            if (ReferenceEquals(node.Value.Entry, entry))
            { RetainedBytes -= node.Value.Bytes; retained.Remove(node); return; }
    }
}
