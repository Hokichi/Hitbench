using Onset.Core.Entities;
using Onset.Services;
using Onset.ViewModels.Shell;

namespace Onset.Test.Presentation;

public class WorkstationNodePasteTests
{
    private WorkstationPageVM vm = null!;
    private Track source = null!, destination = null!;
    private Sample node = null!;
    private StubNodePasteDialogService dialogs = null!;
    [SetUp]
    public async Task Setup()
    {
        node = new Sample { Timestamp = 1000, FileName = "a.wav" };
        source = new Track { SelectedSampleFileName = "a.wav", Samples = [node] };
        destination = new Track { SelectedSampleFileName = "b.wav" };
        var data = new ProjectData { Offsets = [0], BPMs = [120], Tracks = [source, destination] };
        dialogs = new StubNodePasteDialogService();
        vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", data)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["a.wav", "b.wav"]), nodePasteDialog: dialogs, trackRemovalDialog: new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) });
        await vm.LoadAsync("project.json", CancellationToken.None); vm.AcknowledgePresentationReady(vm.LoadGeneration);
    }
    [TearDown] public Task Cleanup() => vm.DeactivateAsync();
    [Test]
    public async Task MarkerPastePreservesOriginalTracksAndFrozenNodeValues()
    {
        var other = new Sample { Timestamp = 1125, FileName = "b.wav" };
        destination.Samples.Add(other);
        vm.SetNodeSelection([node, other]); Assert.That(vm.CopySelectedNodes(), Is.True);
        node.Timestamp = 2000; node.FileName = "b.wav";
        destination.Samples.Remove(other); vm.SelectedTrack = null;
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(3010));
        Assert.That(vm.CanPasteNodes, Is.True);
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.True);
        var first = vm.SelectedSamples.Single(n => n.Timestamp == 3000);
        var second = vm.SelectedSamples.Single(n => n.Timestamp == 3125);
        Assert.That(source.Samples.Contains(first), Is.True);
        Assert.That(destination.Samples.Single(), Is.SameAs(second));
        Assert.That((first.FileName, second.FileName), Is.EqualTo(("a.wav", "b.wav")));
        Assert.That(first, Is.Not.SameAs(node)); Assert.That(second, Is.Not.SameAs(other));
        vm.UndoCommand.Execute(null);
        Assert.That(source.Samples.Single(), Is.SameAs(node)); Assert.That(destination.Samples, Is.Empty);
        vm.RedoCommand.Execute(null);
        Assert.That(source.Samples.Contains(first), Is.True); Assert.That(destination.Samples.Single(), Is.SameAs(second));
    }
    [Test]
    public async Task OffGridOffsetsSurviveDifferentDestinationBpm()
    {
        var other = new Sample { Timestamp = 1001, FileName = "a.wav" }; source.Samples.Add(other);
        vm.SetNodeSelection([node, other]); vm.CopySelectedNodes();
        vm.Project!.Offsets = [0, 2500]; vm.Project.BPMs = [120, 60];
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(3010));
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.True);
        Assert.That(vm.SelectedSamples.Select(n => n.Timestamp).Order(), Is.EqualTo(new[] { 3000, 3001 }));
    }
    [TestCase(1, 3200, 3000)]
    [TestCase(4, 3062.5, 3000)]
    [TestCase(4, 3070, 3125)]
    public async Task PasteUsesCurrentSubdivisionAndEarlierBeatOnTie(int subdivision, double marker, int expected)
    {
        vm.SelectedSubdivisionIndex = subdivision == 1 ? 0 : 3;
        vm.SelectNode(source, node, false); vm.CopySelectedNodes();
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(marker));
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.True);
        Assert.That(vm.SelectedSamples.Single().Timestamp, Is.EqualTo(expected));
    }
    [Test]
    public async Task RepeatedPasteUsesIndependentSnapshot()
    {
        vm.SelectNode(source, node, false); vm.CopySelectedNodes(); source.Samples.Remove(node);
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(3000));
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.True);
        var first = vm.SelectedSamples.Single(); await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(4000));
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.True);
        Assert.That(vm.SelectedSamples.Single(), Is.Not.SameAs(first));
    }
    [Test]
    public async Task TailPasteDiscardsNodesAtAndBeyondAudioEnd()
    {
        var atEnd = new Sample { Timestamp = 1125, FileName = "a.wav" };
        var beyond = new Sample { Timestamp = 1250, FileName = "b.wav" };
        source.Samples.Add(atEnd); destination.Samples.Add(beyond);
        vm.SetNodeSelection([node, atEnd, beyond]); vm.CopySelectedNodes();
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(9900));
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.True);
        var copy = vm.SelectedSamples.Single(); Assert.That(copy.Timestamp, Is.EqualTo(9875));
        Assert.That(destination.Samples.Single(), Is.SameAs(beyond));
        vm.UndoCommand.Execute(null); Assert.That(source.Samples, Is.EqualTo(new[] { node, atEnd }));
        vm.RedoCommand.Execute(null); Assert.That(source.Samples.Last(), Is.SameAs(copy));
    }
    [Test]
    public async Task OverflowOnRemovedTrackDoesNotRejectFittingNodes()
    {
        var other = new Sample { Timestamp = 1250, FileName = "b.wav" }; destination.Samples.Add(other);
        vm.SetNodeSelection([node, other]); vm.CopySelectedNodes();
        await vm.RemoveTrackCommand.ExecuteAsync(destination);
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(9900));
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.True);
        Assert.That(vm.SelectedSamples.Single().Timestamp, Is.EqualTo(9875));
    }
    [Test]
    public async Task SimultaneousNodesOnDifferentTracksAreAllowed()
    {
        var other = new Sample { Timestamp = 1000, FileName = "b.wav" }; destination.Samples.Add(other);
        vm.SetNodeSelection([node, other]); vm.CopySelectedNodes();
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(3000));
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.True);
        Assert.That(vm.SelectedSamples.Select(n => n.Timestamp), Is.EqualTo(new[] { 3000, 3000 }));
    }
    [TestCase("collision")]
    [TestCase("duplicate")]
    [TestCase("track")]
    [TestCase("file")]
    [TestCase("grid")]
    public async Task InvalidRetainedBatchLeavesSelectionAndHistoryAlone(string reason)
    {
        if (reason == "duplicate") source.Samples.Add(new Sample { Timestamp = 1000, FileName = "a.wav" });
        vm.SetNodeSelection(source.Samples); vm.CopySelectedNodes();
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(3000));
        if (reason == "collision") source.Samples.Add(new Sample { Timestamp = 3000, FileName = "a.wav" });
        if (reason == "track") await vm.RemoveTrackCommand.ExecuteAsync(source);
        if (reason == "file") vm.SampleFiles.Remove("a.wav");
        if (reason == "grid") { vm.Project!.Offsets = [11000]; vm.Project.BPMs = [120]; }
        var selected = vm.SelectedSamples.ToArray(); var before = vm.Tracks.SelectMany(t => t.Samples).ToArray();
        var revision = vm.SamplesRevision;
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.False);
        Assert.That(vm.Tracks.SelectMany(t => t.Samples), Is.EqualTo(before));
        Assert.That(vm.SelectedSamples, Is.EquivalentTo(selected));
        Assert.That(vm.SamplesRevision, Is.EqualTo(revision)); Assert.That(vm.CanUndo, Is.False);
    }
    [Test]
    public async Task PasteAndRedoKeepCopiedFileAfterTrackSampleChanges()
    {
        vm.SelectNode(source, node, false); vm.CopySelectedNodes(); vm.SelectSample(source, "b.wav");
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(3000));
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.True);
        var copy = vm.SelectedSamples.Single(); Assert.That(copy.FileName, Is.EqualTo("a.wav"));
        vm.UndoCommand.Execute(null); vm.RedoCommand.Execute(null);
        Assert.That(copy.FileName, Is.EqualTo("a.wav"));
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task PendingCollisionRejectsSecondPasteAndCannotMutateAfterLifecycleChange(bool deactivate)
    {
        var collision = new TaskCompletionSource();
        dialogs.Collision = token => collision.Task.WaitAsync(token);
        vm.SelectNode(source, node, false); vm.CopySelectedNodes();
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(1000));
        var paste = vm.PasteCopiedNodesAsync();
        Assert.That(vm.IsNodeEditBusy, Is.True); Assert.That(await vm.PasteCopiedNodesAsync(), Is.False);
        if (deactivate) await vm.DeactivateAsync();
        else { await vm.LoadAsync("other.json", CancellationToken.None); vm.AcknowledgePresentationReady(vm.LoadGeneration); }
        collision.TrySetResult(); Assert.That(await paste, Is.False);
        Assert.That(source.Samples.Single(), Is.SameAs(node));
        Assert.That(vm.SelectedSamples, Is.Empty); Assert.That(vm.CanUndo, Is.False);
        Assert.That(vm.IsNodeEditBusy, Is.False);
    }
    [TestCase(0, true)]
    [TestCase(125, false)]
    public async Task TransferAdoptsSampleOrRejectsOccupiedTimestamp(int delta, bool succeeds)
    {
        destination.Samples.Add(new Sample { Timestamp = 1125, FileName = "b.wav" });
        vm.SelectNode(source, node, false); vm.BeginNodeDrag(source, node); vm.PreviewNodeDrag(delta, destination);
        Assert.That(await vm.CommitNodeDragAsync(), Is.EqualTo(succeeds));
        Assert.That(node.Timestamp, Is.EqualTo(1000)); if (succeeds)
        {
            Assert.That(source.Samples, Is.Empty); Assert.That(destination.Samples.Contains(node), Is.True);
            Assert.That(node.FileName, Is.EqualTo("b.wav")); vm.UndoCommand.Execute(null);
            Assert.That(source.Samples.Single(), Is.SameAs(node)); Assert.That(node.FileName, Is.EqualTo("a.wav"));
        }
        else { Assert.That(source.Samples.Single(), Is.SameAs(node)); Assert.That(dialogs.Collisions, Is.EqualTo(1)); Assert.That(vm.CanUndo, Is.False); }
    }
}
