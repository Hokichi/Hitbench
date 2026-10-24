using Onset.Core.Entities;
using Onset.Services;

namespace Onset.Test.Services;

public class NodeHistoryManagerTests
{
    [Test] public void FolderRenameKeepsRemovedSamplePlayableAfterRedo()
    {
        var history = new NodeHistoryManager();
        var node = new Sample { FileName = "Piano/C3.wav" };
        var track = new Track { Samples = [node] };
        history.RecordPlacement(track, node, 0);
        Assert.That(history.Undo([track]), Is.True);
        history.RenameSampleFolder("Piano", "RB");
        Assert.That(history.Redo([track]), Is.True);
        Assert.That(track.Samples.Single().FileName, Is.EqualTo("RB/C3.wav"));
    }
    [Test] public void RenamingPreservesCrossTrackUndoAndRedoSnapshots()
    {
        var history = new NodeHistoryManager();
        var node = new Sample { FileName="b.wav", Timestamp=250 };
        var a = new Track { SelectedSampleFileName="a.wav" };
        var b = new Track { SelectedSampleFileName="b.wav", Samples=[node] };
        history.RecordBatch([new(a,node,0,NodeHistoryOperation.Movement,125,250,b,0,"a.wav","b.wav")]);
        history.RenameSample("a.wav","renamed.wav"); a.SelectedSampleFileName="renamed.wav";
        Assert.That(history.Undo([a,b]), Is.True); Assert.That(node.FileName,Is.EqualTo("renamed.wav"));
        history.RenameSample("b.wav","other.wav"); b.SelectedSampleFileName="other.wav";
        Assert.That(history.Redo([a,b]),Is.True); Assert.That(node.FileName,Is.EqualTo("other.wav"));
        Assert.That(node.Timestamp,Is.EqualTo(250)); Assert.That(b.Samples.Single(),Is.SameAs(node));
    }
    [Test]
    public void GlobalEditsUndoInReverseOrder()
    {
        var history = new NodeHistoryManager();
        var a = new Sample();
        var b = new Sample();
        var first = new Track { Samples = [a] };
        var second = new Track { Samples = [b] };
        Track[] tracks = [first, second];
        history.RecordPlacement(first, a, 0);
        history.RecordPlacement(second, b, 0);
        Assert.That(history.Undo(tracks), Is.True);
        Assert.That(second.Samples, Is.Empty);
        Assert.That(first.Samples.Single(), Is.SameAs(a));
        Assert.That(history.Undo(tracks), Is.True);
        Assert.That(first.Samples, Is.Empty);
        Assert.That(history.Redo(tracks), Is.True);
        Assert.That(first.Samples.Single(), Is.SameAs(a));
        Assert.That(history.Redo(tracks), Is.True);
        Assert.That(second.Samples.Single(), Is.SameAs(b));
    }

    [Test]
    public void RemovalRestoresOriginalIndexAndValues()
    {
        var history = new NodeHistoryManager();
        var node = new Sample { Timestamp = 125, FileName = "a.wav" };
        var left = new Sample();
        var right = new Sample();
        var track = new Track { Samples = [left, right] };
        history.RecordRemoval(track, node, 1);
        Assert.That(history.Undo([track]), Is.True);
        Assert.That(track.Samples, Is.EqualTo(new[] { left, node, right }));
        Assert.That(track.Samples[1], Is.SameAs(node));
        Assert.That(node.Timestamp, Is.EqualTo(125));
        Assert.That(node.FileName, Is.EqualTo("a.wav"));
        Assert.That(history.Redo([track]), Is.True);
        Assert.That(track.Samples, Is.EqualTo(new[] { left, right }));
    }

    [Test]
    public void IdenticalNodesRemainDistinct()
    {
        var history = new NodeHistoryManager();
        var a = new Sample { Timestamp = 125, FileName = "a.wav" };
        var b = new Sample { Timestamp = 125, FileName = "a.wav" };
        var track = new Track { Samples = [a, b] };
        history.RecordPlacement(track, b, 1);
        Assert.That(history.Undo([track]), Is.True);
        Assert.That(track.Samples.Single(), Is.SameAs(a));
        Assert.That(history.Redo([track]), Is.True);
        Assert.That(track.Samples[1], Is.SameAs(b));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SelectionAtReinsertionUsesCurrentTrackFile(bool placement)
    {
        var history = new NodeHistoryManager();
        var node = new Sample { FileName = "a.wav" };
        var track = new Track { SelectedSampleFileName = "a.wav" };
        if (placement)
        {
            track.Samples.Add(node);
            history.RecordPlacement(track, node, 0);
            history.Undo([track]);
        }
        else history.RecordRemoval(track, node, 0);
        track.SelectedSampleFileName = "b.wav";
        Assert.That(placement ? history.Redo([track]) : history.Undo([track]), Is.True);
        Assert.That(node.FileName, Is.EqualTo("b.wav"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void RecordClearsRedo(bool placement)
    {
        var history = new NodeHistoryManager();
        var node = new Sample();
        var track = new Track { Samples = [node] };
        history.RecordPlacement(track, node, 0);
        history.Undo([track]);
        Assert.That(history.CanRedo, Is.True);
        if (placement) history.RecordPlacement(track, new Sample(), 0);
        else history.RecordRemoval(track, new Sample(), 0);
        Assert.That(history.CanRedo, Is.False);
        Assert.That(history.CanUndo, Is.True);
    }

    [Test]
    public void ClearEmptiesBothStacks()
    {
        var history = new NodeHistoryManager();
        var track = new Track { Samples = [new Sample(), new Sample()] };
        history.RecordPlacement(track, track.Samples[0], 0);
        history.RecordPlacement(track, track.Samples[1], 1);
        history.Undo([track]);
        history.Clear();
        Assert.That(history.CanUndo, Is.False);
        Assert.That(history.CanRedo, Is.False);
        Assert.That(history.Undo([track]), Is.False);
        Assert.That(history.Redo([track]), Is.False);
    }

    [Test]
    public void RemovedTrackPrunesBothStacksInOrder()
    {
        var history = new NodeHistoryManager();
        var a = new Track();
        var b = new Track();
        var bNodes = new[] { new Sample(), new Sample(), new Sample() };
        foreach (var node in bNodes)
        {
            var aNode = new Sample();
            a.Samples.Add(aNode);
            history.RecordPlacement(a, aNode, a.Samples.Count - 1);
            b.Samples.Add(node);
            history.RecordPlacement(b, node, b.Samples.Count - 1);
        }
        history.Undo([a, b]);
        history.Undo([a, b]);
        history.Undo([a, b]);
        history.RemoveTrack(a);
        Assert.That(history.Undo([b]), Is.True);
        Assert.That(b.Samples, Is.Empty);
        foreach (var node in bNodes)
        {
            Assert.That(history.Redo([b]), Is.True);
            Assert.That(b.Samples.Last(), Is.SameAs(node));
        }
        Assert.That(history.CanRedo, Is.False);
    }

    [TestCase("track")]
    [TestCase("node")]
    [TestCase("duplicate")]
    [TestCase("index")]
    public void InvalidReplayDoesNotAdvanceHistory(string kind)
    {
        var history = new NodeHistoryManager();
        var node = new Sample();
        var track = new Track { Samples = [node] };
        if (kind is "duplicate" or "index")
            history.RecordRemoval(track, node, kind == "index" ? 2 : 0);
        else history.RecordPlacement(track, node, 0);
        if (kind == "node") track.Samples.Clear();
        if (kind == "index") track.Samples.Clear();
        var before = track.Samples.ToArray();
        Assert.That(history.Undo(kind == "track" ? [] : [track]), Is.False);
        Assert.That(track.Samples, Is.EqualTo(before));
        Assert.That(history.CanUndo, Is.True);
        Assert.That(history.CanRedo, Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void InvalidRedoKeepsEntry(bool placement)
    {
        var history = new NodeHistoryManager();
        var node = new Sample();
        var track = new Track();
        if (placement)
        {
            track.Samples.Add(node);
            history.RecordPlacement(track, node, 0);
        }
        else history.RecordRemoval(track, node, 0);
        history.Undo([track]);
        if (placement) track.Samples.Add(node);
        else track.Samples.Clear();
        var before = track.Samples.ToArray();
        Assert.That(history.Redo([track]), Is.False);
        Assert.That(track.Samples, Is.EqualTo(before));
        Assert.That(history.CanRedo, Is.True);
        Assert.That(history.CanUndo, Is.False);
    }
}
