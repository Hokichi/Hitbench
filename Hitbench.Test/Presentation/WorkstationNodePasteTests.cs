using Hitbench.Test;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

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
        var data = new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]), Tracks = [source, destination] };
        dialogs = new StubNodePasteDialogService();
        vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", data)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["a.wav", "b.wav"]), nodePasteDialog: dialogs, trackRemovalDialog: new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) });
        await vm.LoadAsync("project.json", CancellationToken.None); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        vm.SelectedTrack = source;
    }
    [TearDown] public Task Cleanup() => vm.DeactivateAsync();
    [Test]
    public async Task MarkerPasteUsesSelectedTrackAndItsSampleWithFrozenTiming()
    {
        var other = new Sample { Timestamp = 1125, FileName = "b.wav" };
        destination.Samples.Add(other);
        vm.SetNodeSelection([node, other]); Assert.That(vm.CopySelectedNodes(), Is.True);
        node.Timestamp = 2000; node.FileName = "b.wav";
        destination.Samples.Remove(other); vm.SelectedTrack = destination;
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(3010));
        Assert.That(vm.CanPasteNodes, Is.True);
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.True);
        var first = vm.SelectedSamples.Single(n => n.Timestamp == 3000);
        var second = vm.SelectedSamples.Single(n => n.Timestamp == 3125);
        Assert.That(destination.Samples, Is.EqualTo(new[] { first, second }));
        Assert.That((first.FileName, second.FileName), Is.EqualTo(("b.wav", "b.wav")));
        Assert.That(first, Is.Not.SameAs(node)); Assert.That(second, Is.Not.SameAs(other));
        vm.UndoCommand.Execute(null);
        Assert.That(source.Samples.Single(), Is.SameAs(node)); Assert.That(destination.Samples, Is.Empty);
        vm.RedoCommand.Execute(null);
        Assert.That(destination.Samples, Is.EqualTo(new[] { first, second }));
    }
    [Test]
    public async Task OffGridOffsetsSurviveDifferentDestinationBpm()
    {
        var other = new Sample { Timestamp = 1001, FileName = "a.wav" }; source.Samples.Add(other);
        vm.SetNodeSelection([node, other]); vm.CopySelectedNodes();
        vm.Project!.UninheritedPoints = TimingTestData.Replace(vm.Project!.UninheritedPoints, [0, 2500], true); vm.Project.UninheritedPoints = TimingTestData.Replace(vm.Project.UninheritedPoints, [120, 60], false);
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
    public async Task SimultaneousNodesFromDifferentTracksRejectEntirePaste()
    {
        var other = new Sample { Timestamp = 1000, FileName = "b.wav" }; destination.Samples.Add(other);
        vm.SetNodeSelection([node, other]); vm.CopySelectedNodes();
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(3000));
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.False);
        Assert.That(source.Samples.Single(), Is.SameAs(node));
        Assert.That(destination.Samples.Single(), Is.SameAs(other));
        Assert.That(vm.SelectedSamples, Is.EquivalentTo(new[] { node, other }));
        Assert.That(dialogs.Collisions, Is.EqualTo(1)); Assert.That(vm.CanUndo, Is.False);
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
        if (reason == "track") { await vm.RemoveTrackCommand.ExecuteAsync(source); vm.SelectedTrack = null; }
        if (reason == "file") vm.SampleFiles.Remove("a.wav");
        if (reason == "grid") { vm.Project!.UninheritedPoints = TimingTestData.Replace(vm.Project!.UninheritedPoints, [11000], true); vm.Project.UninheritedPoints = TimingTestData.Replace(vm.Project.UninheritedPoints, [120], false); }
        var selected = vm.SelectedSamples.ToArray(); var before = vm.Tracks.SelectMany(t => t.Samples).ToArray();
        var revision = vm.SamplesRevision;
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.False);
        Assert.That(vm.Tracks.SelectMany(t => t.Samples), Is.EqualTo(before));
        Assert.That(vm.SelectedSamples, Is.EquivalentTo(selected));
        Assert.That(vm.SamplesRevision, Is.EqualTo(revision)); Assert.That(vm.CanUndo, Is.False);
    }
    [Test]
    public async Task PasteAndRedoFollowCurrentTrackSample()
    {
        vm.SelectNode(source, node, false); vm.CopySelectedNodes(); vm.SelectSample(source, "b.wav");
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(3000));
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.True);
        var copy = vm.SelectedSamples.Single(); Assert.That(copy.FileName, Is.EqualTo("b.wav"));
        vm.UndoCommand.Execute(null); vm.SelectSample(source, "a.wav"); vm.RedoCommand.Execute(null);
        Assert.That(copy.FileName, Is.EqualTo("a.wav"));
    }

    [TestCase("none")]
    [TestCase("instrumental")]
    [TestCase("missing")]
    [TestCase("unassigned")]
    [TestCase("removed")]
    public async Task PasteRequiresEditableSelectedTrack(string kind)
    {
        vm.SelectNode(source, node, false); vm.CopySelectedNodes();
        vm.SelectedTrack = destination;
        if (kind == "none") vm.SelectedTrack = null;
        if (kind == "instrumental") { var track = new InstrumentalTrack(); vm.Tracks.Add(track); vm.SelectedTrack = track; }
        if (kind == "missing") destination.SelectedSampleFileName = "missing.wav";
        if (kind == "unassigned") destination.SelectedSampleFileName = null;
        if (kind == "removed") vm.Tracks.Remove(destination);
        Assert.That(vm.CanPasteNodes, Is.False);
        Assert.That(vm.PasteNodesCommand.CanExecute(null), Is.False);
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.False);
        Assert.That(source.Samples.Single(), Is.SameAs(node));
        Assert.That(vm.CanUndo, Is.False);
    }

    [Test]
    public async Task RemovingSourceAfterCopyDoesNotPreventPasteToSelectedTrack()
    {
        vm.SelectNode(source, node, false); vm.CopySelectedNodes();
        await vm.RemoveTrackCommand.ExecuteAsync(source); vm.SelectedTrack = destination;
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(3000));
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.True);
        Assert.That(destination.Samples.Single().Timestamp, Is.EqualTo(3000));
        Assert.That(destination.Samples.Single().FileName, Is.EqualTo("b.wav"));
    }

    [Test]
    public void PasteAvailabilityNotifiesOnTrackAndSampleChanges()
    {
        vm.SelectNode(source, node, false); vm.CopySelectedNodes();
        var notifications = 0;
        vm.PasteNodesCommand.CanExecuteChanged += (_, _) => notifications++;
        vm.SelectedTrack = null;
        Assert.That(vm.CanPasteNodes, Is.False);
        vm.SelectedTrack = destination;
        Assert.That(vm.CanPasteNodes, Is.True);
        var before = notifications;
        vm.SelectSample(destination, "a.wav");
        Assert.That(destination.SelectedSampleFileName, Is.EqualTo("a.wav"));
        Assert.That(notifications, Is.GreaterThan(before));
        destination.SelectedSampleFileName = "missing.wav";
        Assert.That(vm.CanPasteNodes, Is.False);
        vm.SelectSample(destination, "b.wav");
        Assert.That(vm.CanPasteNodes, Is.True);
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
