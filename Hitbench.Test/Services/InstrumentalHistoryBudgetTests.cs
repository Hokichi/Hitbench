using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class InstrumentalHistoryBudgetTests
{
    [Test]
    public void AccountingCoversTheOwnedTransactionContainers()
    {
        var history = new InstrumentalNoteHistoryManager();
        var note = new InstrumentalNoteSnapshot(Guid.NewGuid(), new string(['C', '4']), 125);
        InstrumentalNoteSnapshot[] after = [note]; Guid[] affected = [note.Id];
        history.Record([], after, affected); history.Clear();
        var before = GC.GetAllocatedBytesForCurrentThread();
        history.Record([], after, affected);
        var ownedContainers = GC.GetAllocatedBytesForCurrentThread() - before;
        // The pre-existing snapshot/key are retained too, beyond these new containers.
        Assert.That(InstrumentalNoteHistoryEntry.EstimateRetainedBytes(0, 1, 1), Is.GreaterThanOrEqualTo(ownedContainers + 96));
    }

    [Test]
    public void ManyEditsAcrossInactiveTracksCannotMultiplyRetention()
    {
        var budget = new InstrumentalHistoryBudget(1600);
        var histories = Enumerable.Range(0, 4).Select(_ => new InstrumentalNoteHistoryManager(budget)).ToArray();
        var states = Enumerable.Range(0, 4).Select(_ => new[] { new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 0) }).ToArray();
        for (var i = 1; i <= 1000; i++)
        {
            var index = i % histories.Length;
            var next = new[] { states[index][0] with { Timestamp = i } };
            histories[index].Record(states[index], next, [next[0].Id]); states[index] = next;
            Assert.That(budget.RetainedBytes, Is.LessThanOrEqualTo(1600));
        }
        Assert.That(histories.Count(history => history.CanUndo), Is.EqualTo(2));
        foreach (var history in histories) history.Clear();
        Assert.That(budget.RetainedBytes, Is.Zero);
    }

    [Test]
    public void PressureTrimsDependentRedoInsteadOfAdvertisingBrokenReplay()
    {
        var budget = new InstrumentalHistoryBudget(2500);
        var first = new InstrumentalNoteHistoryManager(budget);
        var other = new InstrumentalNoteHistoryManager(budget);
        var note = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 0);
        for (var i = 1; i <= 3; i++) first.Record([note with { Timestamp = i - 1 }], [note with { Timestamp = i }], [note.Id]);
        first.Undo([note with { Timestamp = 3 }], _ => true, out _);
        first.Undo([note with { Timestamp = 2 }], _ => true, out _);
        var notes = Enumerable.Range(0, 8).Select(i => note with { Id = Guid.NewGuid(), Timestamp = i }).ToArray();
        other.Record([], notes, notes.Select(n => n.Id).ToArray());
        Assert.That(first.CanRedo, Is.False);
        Assert.That(first.CanUndo, Is.False);
        Assert.That(budget.RetainedBytes, Is.LessThanOrEqualTo(2500));
    }

    [Test]
    public void SeveralTrackHistoriesShareOneCeilingAndReleaseOnClear()
    {
        var budget = new InstrumentalHistoryBudget(900);
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
        var budget = new InstrumentalHistoryBudget(900);
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
