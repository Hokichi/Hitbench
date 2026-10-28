using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class InstrumentalHistoryBudgetTests
{
    [Test]
    public void SeveralTrackHistoriesShareOneCeilingAndReleaseOnClear()
    {
        var budget = new InstrumentalHistoryBudget(600);
        var first = new InstrumentalNoteHistoryManager(budget);
        var second = new InstrumentalNoteHistoryManager(budget);
        var note = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 125);
        first.Record([], [note], [note.Id]);
        second.Record([], [note], [note.Id]);
        Assert.That(first.CanUndo, Is.False);
        Assert.That(second.CanUndo, Is.True);
        var charged = budget.RetainedBytes;
        Assert.That(second.Undo([note], _ => true, out _), Is.True);
        Assert.That(budget.RetainedBytes, Is.EqualTo(charged));
        Assert.That(second.Redo([], _ => true, out _), Is.True);
        second.Clear();
        Assert.That(budget.RetainedBytes, Is.Zero);
    }

    [Test]
    public void OversizedEditClearsIncompatibleUndoAndNoOpPreservesRedo()
    {
        var budget = new InstrumentalHistoryBudget(600);
        var manager = new InstrumentalNoteHistoryManager(budget);
        var note = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 125);
        manager.Record([], [note], [note.Id]);
        Assert.That(manager.Undo([note], _ => true, out _), Is.True);
        manager.Record([], [], []);
        Assert.That(manager.CanRedo, Is.True);
        manager.Record([], Enumerable.Range(0, 20).Select(i => note with { Timestamp = i }).ToArray(), []);
        Assert.That(manager.CanUndo, Is.False);
        Assert.That(manager.CanRedo, Is.False);
        Assert.That(budget.RetainedBytes, Is.Zero);
    }
}
