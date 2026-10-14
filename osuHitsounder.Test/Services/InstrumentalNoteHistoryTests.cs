using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
namespace osuHitsounder.Test.Services;

public class InstrumentalNoteHistoryTests
{
    [Test]
    public void ReplayIsAtomicAndNoOpPreservesRedo()
    {
        var note = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 125, 70);
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
        var note = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 125, 70);
        var history = new InstrumentalNoteHistoryManager(); history.Record([], [note], [note.Id]);
        Assert.That(history.Undo([note with { Volume = 60 }], _ => throw new Exception(), out _), Is.False);
        Assert.That(history.CanUndo, Is.True);
    }
}
