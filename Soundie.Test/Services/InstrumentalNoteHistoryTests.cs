using Soundie.Resources.DataModels;
using Soundie.Services;
namespace Soundie.Test.Services;

public class InstrumentalNoteHistoryTests
{
    [Test]
    public void LargeReplayPreservesRequestedSelectionOrder()
    {
        var before = Enumerable.Range(0, 4000).Select(i => new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", i)).ToArray();
        var after = before.Select(n => n with { Timestamp = n.Timestamp + 5000 }).ToArray();
        var ids = before.Reverse().Select(n => n.Id).Append(Guid.NewGuid()).ToArray();
        var history = new InstrumentalNoteHistoryManager(); history.Record(before, after, ids);
        Assert.That(history.Undo(after, _ => true, out var selected), Is.True);
        Assert.That(selected, Is.EqualTo(ids.Take(4000)));
    }
    [Test]
    public void ReplayIsAtomicAndNoOpPreservesRedo()
    {
        var note = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 125);
        var history = new InstrumentalNoteHistoryManager();
        history.Record([], [note], [note.Id]);
        Assert.That(history.Undo([note], _ => false, out _), Is.False);
        Assert.That(history.CanUndo, Is.True);
        IReadOnlyList<InstrumentalNoteSnapshot> restored = [note];
        Assert.That(history.Undo([note], notes => { restored = notes; return true; }, out var ids), Is.True);
        Assert.That(restored, Is.Empty);
        Assert.That(ids, Is.Empty);
        history.Record([], [], []);
        Assert.That(history.CanRedo, Is.True);
        Assert.That(history.Redo([], notes => { restored = notes; return true; }, out ids), Is.True);
        Assert.That(restored.Single(), Is.EqualTo(note));
        Assert.That(ids, Is.EqualTo(new[] { note.Id }));
    }
    [Test]
    public void ReplayRejectsUnexpectedState()
    {
        var note = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 125);
        var history = new InstrumentalNoteHistoryManager(); history.Record([], [note], [note.Id]);
        Assert.That(history.Undo([note with { Timestamp = 250 }], _ => throw new Exception(), out _), Is.False);
        Assert.That(history.CanUndo, Is.True);
    }
}
