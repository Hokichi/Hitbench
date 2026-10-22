using Soundie.Core.Entities;
using Soundie.Services;

namespace Soundie.Test.Services;

public class NodeHistoryBatchTests
{
    [Test]
    public void FolderRenameRewritesEachHistoryEntryOnceAndRetainsIdentity()
    {
        var history = new NodeHistoryManager();
        var samples = Enumerable.Range(0, 500).Select(i => new Sample { FileName = $"piano/nested/{i}.wav", Timestamp = i }).ToList();
        var track = new Track { Samples = samples };
        var identities = samples.ToArray();
        history.RecordBatch(samples.Select((s, i) => new NodeHistoryEntry(track, s, i, NodeHistoryOperation.Placement, NextFileName: s.FileName)).ToArray());
        var before = history.RewrittenEntryCount;
        history.RenameSampleFolder("PIANO", "keys");
        Assert.That(history.RewrittenEntryCount - before, Is.EqualTo(500));
        Assert.That(history.Undo([track]), Is.True);
        Assert.That(history.Redo([track]), Is.True);
        for (var i = 0; i < samples.Count; i++)
        {
            Assert.That(track.Samples[i], Is.SameAs(identities[i]));
            Assert.That(track.Samples[i].FileName, Is.EqualTo($"keys/nested/{i}.wav"));
        }
    }
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void RemovingEitherTransferTrackPrunesUndoAndRedo(bool removeDestination, bool redo)
    {
        var node = new Sample { Timestamp = 500, FileName = "b.wav" };
        var source = new Track(); var destination = new Track { Samples = [node] };
        var history = new NodeHistoryManager();
        history.RecordBatch([new(source, node, 0, NodeHistoryOperation.Movement, 125, 500, destination, 0, "a.wav", "b.wav")]);
        if (redo) Assert.That(history.Undo([source, destination]), Is.True);
        history.RemoveTrack(removeDestination ? destination : source);
        Assert.That(history.CanUndo, Is.False); Assert.That(history.CanRedo, Is.False);
        Assert.That(source.Samples.Count, Is.EqualTo(redo ? 1 : 0));
        Assert.That(destination.Samples.Count, Is.EqualTo(redo ? 0 : 1));
    }
    [Test]
    public void PinnedPlacementRedoKeepsCopiedFile()
    {
        var sample = new Sample { Timestamp = 500, FileName = "a.wav" };
        var track = new Track { Samples = [sample], SelectedSampleFileName = "b.wav" };
        var history = new NodeHistoryManager();
        history.RecordBatch([new(track, sample, 0, NodeHistoryOperation.Placement, NextFileName: "a.wav")]);
        Assert.That(history.Undo([track]), Is.True); Assert.That(history.Redo([track]), Is.True);
        Assert.That(sample.FileName, Is.EqualTo("a.wav"));
    }

    [Test]
    public void TransferUndoRestoresSourceTrackTimeAndFile()
    {
        var sample = new Sample { Timestamp = 500, FileName = "b.wav" };
        var source = new Track { SelectedSampleFileName = "a.wav" };
        var destination = new Track { SelectedSampleFileName = "b.wav", Samples = [sample] };
        var history = new NodeHistoryManager();
        history.RecordBatch([new(source, sample, 0, NodeHistoryOperation.Movement, 125, 500, destination, 0, "a.wav", "b.wav")]);
        Assert.That(history.Undo([source, destination]), Is.True);
        Assert.That(source.Samples.Single(), Is.SameAs(sample));
        Assert.That(destination.Samples, Is.Empty);
        Assert.That(sample.Timestamp, Is.EqualTo(125));
        Assert.That(sample.FileName, Is.EqualTo("a.wav"));
        Assert.That(history.Redo([source, destination]), Is.True);
        Assert.That(destination.Samples.Single(), Is.SameAs(sample));
    }

    [Test]
    public void MovementReplaysWholeGroupWithOriginalIdentity()
    {
        var a = new Sample { Timestamp = 375 };
        var b = new Sample { Timestamp = 500 };
        var first = new Track { Samples = [a] };
        var second = new Track { Samples = [b] };
        var history = new NodeHistoryManager();
        history.RecordBatch([new(first, a, 0, NodeHistoryOperation.Movement, 125, 375), new(second, b, 0, NodeHistoryOperation.Movement, 250, 500)]);
        Assert.That(history.Undo([first, second]), Is.True);
        Assert.That(new[] { a.Timestamp, b.Timestamp }, Is.EqualTo(new[] { 125, 250 }));
        Assert.That(history.Redo([first, second]), Is.True);
        Assert.That(new[] { a.Timestamp, b.Timestamp }, Is.EqualTo(new[] { 375, 500 }));
        Assert.That(first.Samples.Single(), Is.SameAs(a));
    }

    [Test]
    public void RemovalRestoresAdjacentIndexesAndCurrentTrackFile()
    {
        var left = new Sample(); var a = new Sample(); var b = new Sample(); var right = new Sample();
        var track = new Track { Samples = [left, right], SelectedSampleFileName = "current.wav" };
        var history = new NodeHistoryManager();
        history.RecordBatch([new(track, b, 2, NodeHistoryOperation.Removal), new(track, a, 1, NodeHistoryOperation.Removal)]);
        Assert.That(history.Undo([track]), Is.True);
        Assert.That(track.Samples, Is.EqualTo(new[] { left, a, b, right }));
        Assert.That(a.FileName, Is.EqualTo("current.wav"));
        Assert.That(history.Redo([track]), Is.True);
        Assert.That(track.Samples, Is.EqualTo(new[] { left, right }));
    }

    [Test]
    public void InvalidLaterMemberDoesNotPartiallyReplayOrAdvance()
    {
        var a = new Sample { Timestamp = 375 }; var b = new Sample { Timestamp = 999 };
        var first = new Track { Samples = [a] }; var second = new Track { Samples = [b] };
        var history = new NodeHistoryManager();
        history.RecordBatch([new(first, a, 0, NodeHistoryOperation.Movement, 125, 375), new(second, b, 0, NodeHistoryOperation.Movement, 250, 500)]);
        Assert.That(history.Undo([first, second]), Is.False);
        Assert.That(a.Timestamp, Is.EqualTo(375));
        Assert.That(history.CanUndo, Is.True);
        Assert.That(history.CanRedo, Is.False);
    }

    [Test]
    public void PlacementBatchPrunesOnlyRemovedTrackAndPreservesRedo()
    {
        var a = new Sample(); var b = new Sample(); var c = new Sample();
        var first = new Track { Samples = [a] }; var second = new Track { Samples = [b, c] };
        var history = new NodeHistoryManager();
        history.RecordBatch([new(first, a, 0, NodeHistoryOperation.Placement), new(second, b, 0, NodeHistoryOperation.Placement)]);
        history.RecordPlacement(second, c, 1);
        history.Undo([first, second]);
        history.RemoveTrack(first);
        Assert.That(history.Undo([second]), Is.True);
        Assert.That(second.Samples, Is.Empty);
        history.RecordBatch([]);
        Assert.That(history.CanRedo, Is.True);
        Assert.That(history.Redo([second]), Is.True);
        Assert.That(second.Samples.Single(), Is.SameAs(b));
        Assert.That(history.Redo([second]), Is.True);
        Assert.That(second.Samples.Last(), Is.SameAs(c));
    }

    [Test]
    public void NoOpMovementDoesNotInvalidateRedo()
    {
        var node = new Sample { Timestamp = 125 };
        var track = new Track { Samples = [node] };
        var history = new NodeHistoryManager();
        history.RecordPlacement(track, node, 0);
        history.Undo([track]);
        history.RecordBatch([new(track, node, 0, NodeHistoryOperation.Movement, 125, 125)]);
        Assert.That(history.CanRedo, Is.True);
        Assert.That(history.CanUndo, Is.False);
    }
}
