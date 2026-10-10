using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class WorkstationNodePasteTests
{
    private WorkstationPageVM vm = null!;
    private Track source = null!, destination = null!;
    private Sample node = null!;
    private StubNodePasteDialogService dialogs = null!;
    [SetUp]
    public async Task Setup()
    {
        node = new Sample { Timestamp = 1000, FileName = "a.wav", Volume = 37 };
        source = new Track { SelectedSampleFileName = "a.wav", Samples = [node] };
        destination = new Track { SelectedSampleFileName = "b.wav" };
        var data = new ProjectData { Offsets = [0], BPMs = [120], Tracks = [source, destination] };
        dialogs = new StubNodePasteDialogService();
        vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", data)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["a.wav", "b.wav"]), nodePasteDialog: dialogs, trackRemovalDialog: new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) });
        await vm.LoadAsync("project.json", CancellationToken.None); vm.AcknowledgePresentationReady(vm.LoadGeneration);
    }
    [TearDown] public Task Cleanup() => vm.DeactivateAsync();
    [TestCase(NodePasteTiming.SourceTimestamps, 1000)]
    [TestCase(NodePasteTiming.PlaybackMarker, 3000)]
    public async Task CrossTrackPasteUsesChosenTimingAndDestinationSample(NodePasteTiming timing, int expected)
    {
        vm.SelectNode(source, node, false); vm.CopySelectedNodes();
        node.Timestamp = 2000; node.Volume = 99;
        vm.SelectedTrack = destination; dialogs.Choice = timing;
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(3010));
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.True);
        var copy = destination.Samples.Single();
        Assert.That(copy.Timestamp, Is.EqualTo(expected));
        Assert.That(copy.FileName, Is.EqualTo("b.wav")); Assert.That(copy.Volume, Is.EqualTo(37));
        Assert.That(vm.SelectedSamples.Single(), Is.SameAs(copy)); Assert.That(dialogs.Choices, Is.EqualTo(1));
        vm.UndoCommand.Execute(null); Assert.That(destination.Samples, Is.Empty);
        vm.RedoCommand.Execute(null); Assert.That(destination.Samples.Single(), Is.SameAs(copy));
    }
    [Test]
    public async Task ExistingDestinationCancelsPasteAndRetainsSelection()
    {
        var existing = new Sample { Timestamp = 1000, FileName = "b.wav" }; destination.Samples.Add(existing);
        vm.SelectNode(source, node, false); vm.CopySelectedNodes(); vm.SelectedTrack = destination;
        dialogs.Choice = NodePasteTiming.SourceTimestamps;
        var revision = vm.SamplesRevision;
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.False);
        Assert.That(destination.Samples.Single(), Is.SameAs(existing));
        Assert.That(vm.SelectedSamples.Single(), Is.SameAs(node)); Assert.That(vm.SamplesRevision, Is.EqualTo(revision));
        Assert.That(vm.CanUndo, Is.False); Assert.That(vm.ErrorMessage, Is.EqualTo("Existing sample nodes found."));
        Assert.That(dialogs.Collisions, Is.EqualTo(1));
    }
    [Test]
    public async Task SnappingTwoCopiesOntoSameTimestampCancelsWholeBatch()
    {
        var other = new Sample { Timestamp = 1001, FileName = "a.wav" }; source.Samples.Add(other);
        vm.SetNodeSelection([node, other]); vm.CopySelectedNodes(); vm.SelectedTrack = destination;
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(3000));
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.False);
        Assert.That(destination.Samples, Is.Empty); Assert.That(source.Samples, Is.EqualTo(new[] { node, other }));
        Assert.That(vm.CanUndo, Is.False); Assert.That(dialogs.Collisions, Is.EqualTo(1));
    }
    [Test]
    public async Task FrozenCopySurvivesSourceRemoval()
    {
        vm.SelectNode(source, node, false); vm.CopySelectedNodes(); vm.SelectedTrack = destination;
        await vm.RemoveTrackCommand.ExecuteAsync(source); dialogs.Choice = NodePasteTiming.SourceTimestamps;
        Assert.That(vm.Project!.Tracks.Contains(source), Is.False);
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.True);
        Assert.That(destination.Samples.Single().Timestamp, Is.EqualTo(1000));
    }
    [TestCase(NodePasteTiming.SourceTimestamps)]
    [TestCase(NodePasteTiming.PlaybackMarker)]
    public async Task UnusableGridRejectsBothTimingModes(NodePasteTiming timing)
    {
        vm.SelectNode(source, node, false); vm.CopySelectedNodes(); vm.SelectedTrack = destination;
        dialogs.Choice = timing; vm.Project!.Offsets = []; vm.Project.BPMs = [];
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.False);
        Assert.That(destination.Samples, Is.Empty); Assert.That(vm.CanUndo, Is.False);
        Assert.That(vm.SelectedSamples.Single(), Is.SameAs(node));
    }
    [TestCase(null)]
    [TestCase((NodePasteTiming)999)]
    public async Task CancelledOrInvalidModalChoiceChangesNothing(NodePasteTiming? choice)
    {
        vm.SelectNode(source, node, false); vm.CopySelectedNodes(); vm.SelectedTrack = destination; dialogs.Choice = choice;
        Assert.That(await vm.PasteCopiedNodesAsync(), Is.False);
        Assert.That(destination.Samples, Is.Empty); Assert.That(vm.SelectedSamples.Single(), Is.SameAs(node));
        Assert.That(vm.CanUndo, Is.False);
    }
    [Test]
    public async Task PendingChoiceCapturesMarkerAndRejectsSecondInvocation()
    {
        var choice = new TaskCompletionSource<NodePasteTiming?>(); dialogs.Choose = _ => choice.Task;
        vm.SelectNode(source, node, false); vm.CopySelectedNodes(); vm.SelectedTrack = destination;
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(3010));
        var paste = vm.PasteCopiedNodesAsync();
        Assert.That(vm.IsNodeEditBusy, Is.True); Assert.That(await vm.PasteCopiedNodesAsync(), Is.False);
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(5000));
        choice.SetResult(NodePasteTiming.PlaybackMarker);
        Assert.That(await paste, Is.True); Assert.That(destination.Samples.Single().Timestamp, Is.EqualTo(3000));
    }
    [Test]
    public async Task ReloadWhileChoicePendingRejectsOldResult()
    {
        var choice = new TaskCompletionSource<NodePasteTiming?>(); dialogs.Choose = _ => choice.Task;
        vm.SelectNode(source, node, false); vm.CopySelectedNodes(); vm.SelectedTrack = destination;
        var paste = vm.PasteCopiedNodesAsync();
        await vm.LoadAsync("other.json", CancellationToken.None); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        choice.SetResult(NodePasteTiming.SourceTimestamps);
        Assert.That(await paste, Is.False); Assert.That(destination.Samples, Is.Empty);
        Assert.That(vm.IsNodeEditBusy, Is.False);
    }
    [TestCase(0, true)]
    [TestCase(125, false)]
    public async Task TransferAdoptsSampleOrRejectsOccupiedTimestamp(int delta, bool succeeds)
    {
        destination.Samples.Add(new Sample { Timestamp = 1125, FileName = "b.wav" });
        vm.SelectNode(source, node, false); vm.BeginNodeDrag(source, node); vm.PreviewNodeDrag(delta, destination);
        Assert.That(await vm.CommitNodeDragAsync(), Is.EqualTo(succeeds));
        Assert.That(node.Timestamp, Is.EqualTo(1000)); Assert.That(node.Volume, Is.EqualTo(37));
        if (succeeds)
        {
            Assert.That(source.Samples, Is.Empty); Assert.That(destination.Samples.Contains(node), Is.True);
            Assert.That(node.FileName, Is.EqualTo("b.wav")); vm.UndoCommand.Execute(null);
            Assert.That(source.Samples.Single(), Is.SameAs(node)); Assert.That(node.FileName, Is.EqualTo("a.wav"));
        }
        else { Assert.That(source.Samples.Single(), Is.SameAs(node)); Assert.That(dialogs.Collisions, Is.EqualTo(1)); Assert.That(vm.CanUndo, Is.False); }
    }
}
