namespace Hitbench.Services;

public sealed class InstrumentalHistoryBudget
{
    private readonly LinkedList<InstrumentalNoteHistoryEntry> retained = new();
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
        while (RetainedBytes > MaximumBytes - bytes) retained.First!.Value.Evict!();
        return true;
    }
    internal void Add(InstrumentalNoteHistoryEntry entry, long bytes, Action evict)
    {
        entry.RetainedBytes = bytes; entry.Evict = evict;
        entry.BudgetNode = retained.AddLast(entry);
        RetainedBytes = checked(RetainedBytes + bytes);
    }
    internal void Remove(InstrumentalNoteHistoryEntry entry)
    {
        if (entry.BudgetNode is not { } node || !ReferenceEquals(node.List, retained)) return;
        RetainedBytes -= entry.RetainedBytes; retained.Remove(node);
        entry.BudgetNode = null; entry.Evict = null;
    }
}
