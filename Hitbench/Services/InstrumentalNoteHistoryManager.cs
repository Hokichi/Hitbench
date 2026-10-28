using Hitbench.Resources.DataModels;
namespace Hitbench.Services;

public sealed class InstrumentalNoteHistoryManager(InstrumentalHistoryBudget? budget = null)
{
    private readonly InstrumentalHistoryBudget budget = budget ?? new();
    private readonly LinkedList<InstrumentalNoteHistoryEntry> undo = new(), redo = new();
    public event Action? Changed;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public void Record(IReadOnlyList<InstrumentalNoteSnapshot> before, IReadOnlyList<InstrumentalNoteSnapshot> after, IReadOnlyList<Guid> affectedIds)
    {
        if (before.SequenceEqual(after)) return;
        Release(redo);
        var bytes = InstrumentalNoteHistoryEntry.EstimateRetainedBytes(before.Count, after.Count, affectedIds.Count);
        if (!budget.MakeRoom(bytes)) { Clear(); return; }
        var entry = new InstrumentalNoteHistoryEntry(before, after, affectedIds);
        undo.AddLast(entry);
        budget.Add(entry, bytes, () => Evict(entry));
        Changed?.Invoke();
    }
    public bool Undo(IReadOnlyList<InstrumentalNoteSnapshot> current, Func<IReadOnlyList<InstrumentalNoteSnapshot>, bool> apply, out IReadOnlyList<Guid> selectedIds) => Replay(undo, redo, current, apply, true, out selectedIds);
    public bool Redo(IReadOnlyList<InstrumentalNoteSnapshot> current, Func<IReadOnlyList<InstrumentalNoteSnapshot>, bool> apply, out IReadOnlyList<Guid> selectedIds) => Replay(redo, undo, current, apply, false, out selectedIds);
    private bool Replay(LinkedList<InstrumentalNoteHistoryEntry> source, LinkedList<InstrumentalNoteHistoryEntry> destination, IReadOnlyList<InstrumentalNoteSnapshot> current, Func<IReadOnlyList<InstrumentalNoteSnapshot>, bool> apply, bool reverse, out IReadOnlyList<Guid> selectedIds)
    {
        selectedIds = [];
        if (source.Last is not { } node || !current.SequenceEqual(reverse ? node.Value.After : node.Value.Before)) return false;
        var entry = node.Value;
        var restored = reverse ? entry.Before : entry.After;
        if (!apply(restored)) return false;
        source.Remove(node); destination.AddLast(node);
        var restoredIds = restored.Select(n => n.Id).ToHashSet();
        selectedIds = Array.AsReadOnly(entry.AffectedNoteIds.Where(restoredIds.Contains).ToArray());
        Changed?.Invoke();
        return true;
    }
    private void Evict(InstrumentalNoteHistoryEntry entry)
    {
        if (undo.Remove(entry)) budget.Remove(entry);
        else
        {
            while (redo.First is { } node)
            {
                redo.RemoveFirst(); budget.Remove(node.Value);
                if (ReferenceEquals(node.Value, entry)) break;
            }
        }
        Changed?.Invoke();
    }
    private void Release(LinkedList<InstrumentalNoteHistoryEntry> entries)
    {
        foreach (var entry in entries) budget.Remove(entry);
        entries.Clear();
    }
    public void Clear() { Release(undo); Release(redo); Changed?.Invoke(); }
}
