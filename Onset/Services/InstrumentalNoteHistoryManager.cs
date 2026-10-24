using Onset.Resources.DataModels;
namespace Onset.Services;

public sealed class InstrumentalNoteHistoryManager
{
    private readonly Stack<InstrumentalNoteHistoryEntry> undo = new(), redo = new();
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public void Record(IReadOnlyList<InstrumentalNoteSnapshot> before, IReadOnlyList<InstrumentalNoteSnapshot> after, IReadOnlyList<Guid> affectedIds)
    {
        if (before.SequenceEqual(after)) return;
        undo.Push(new(before, after, affectedIds)); redo.Clear();
    }
    public bool Undo(IReadOnlyList<InstrumentalNoteSnapshot> current, Func<IReadOnlyList<InstrumentalNoteSnapshot>, bool> apply, out IReadOnlyList<Guid> selectedIds) => Replay(undo, redo, current, apply, true, out selectedIds);
    public bool Redo(IReadOnlyList<InstrumentalNoteSnapshot> current, Func<IReadOnlyList<InstrumentalNoteSnapshot>, bool> apply, out IReadOnlyList<Guid> selectedIds) => Replay(redo, undo, current, apply, false, out selectedIds);
    private static bool Replay(Stack<InstrumentalNoteHistoryEntry> source, Stack<InstrumentalNoteHistoryEntry> destination, IReadOnlyList<InstrumentalNoteSnapshot> current, Func<IReadOnlyList<InstrumentalNoteSnapshot>, bool> apply, bool reverse, out IReadOnlyList<Guid> selectedIds)
    {
        selectedIds = [];
        if (!source.TryPeek(out var entry) || !current.SequenceEqual(reverse ? entry.After : entry.Before)) return false;
        var restored = reverse ? entry.Before : entry.After;
        if (!apply(restored)) return false;
        source.Pop(); destination.Push(entry);
        var restoredIds = restored.Select(n => n.Id).ToHashSet();
        selectedIds = Array.AsReadOnly(entry.AffectedNoteIds.Where(restoredIds.Contains).ToArray());
        return true;
    }
    public void Clear() { undo.Clear(); redo.Clear(); }
}
