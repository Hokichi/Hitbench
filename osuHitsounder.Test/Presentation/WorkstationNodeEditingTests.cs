using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class WorkstationNodeEditingTests
{
    private WorkstationPageVM vm = null!;
    private StubWorkstationAudioService audio = null!;
    private DelayedProjectFileService saves = null!;
    private Track first = null!, second = null!;
    private Sample a = null!, b = null!, c = null!;

    [SetUp]
    public async Task Setup()
    {
        a = new Sample { Timestamp = 1000, FileName = "a.wav", Volume = 37 };
        b = new Sample { Timestamp = 1250, FileName = "a.wav" };
        c = new Sample { Timestamp = 1125, FileName = "b.wav" };
        first = new Track { SelectedSampleFileName = "a.wav", Samples = [a, b] };
        second = new Track { SelectedSampleFileName = "b.wav", Samples = [c] };
        var project = new ProjectData { Offsets = [0], BPMs = [120], Tracks = [first, second] };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", project)) };
        audio = new StubWorkstationAudioService();
        saves = new DelayedProjectFileService(); saves.Release();
        vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(),
            new StubSampleFileService(["a.wav", "b.wav"]), projectFiles: saves,
            trackRemovalDialog: new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) });
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        await vm.FlushPlaybackUpdatesAsync(); await vm.FlushSavesAsync();
    }

    [TearDown]
    public async Task Cleanup() { await vm.DeactivateAsync(); audio.Dispose(); }

    [Test]
    public void SelectionUsesLiveIdentityAndToggle()
    {
        Assert.That(vm.SelectNode(first, a, false), Is.True);
        Assert.That(vm.SelectNode(second, c, true), Is.True);
        Assert.That(vm.SelectedSamples, Is.EquivalentTo(new[] { a, c }));
        vm.SelectNode(first, a, true);
        Assert.That(vm.SelectedSamples.Single(), Is.SameAs(c));
        Assert.That(vm.SelectNode(first, new Sample { Timestamp = 1000 }, false), Is.False);
        vm.SetNodeSelection([a, a, c, new Sample()]);
        Assert.That(vm.SelectedSamples, Is.EquivalentTo(new[] { a, c }));
    }

    [Test]
    public async Task PastePreservesFormationFrozenValuesAndOriginalTracks()
    {
        vm.SetNodeSelection([a, b, c]);
        Assert.That(vm.CopySelectedNodes(), Is.True);
        a.Timestamp = 2000; a.Volume = 99; a.FileName = "b.wav";
        first.Samples.Remove(b);
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(3010));
        Assert.That(vm.PasteCopiedNodes(), Is.True);
        Assert.That(vm.SelectedSamples.Select(n => n.Timestamp).Order(), Is.EqualTo(new[] { 3000, 3125, 3250 }));
        var earliest = vm.SelectedSamples.Single(n => n.Timestamp == 3000);
        Assert.That(earliest.FileName, Is.EqualTo("a.wav"));
        Assert.That(earliest.Volume, Is.EqualTo(37));
        Assert.That(first.Samples.Contains(earliest), Is.True);
        Assert.That(second.Samples.Any(n => n.Timestamp == 3125), Is.True);
        var copies = vm.SelectedSamples.ToArray();
        Assert.That(vm.PasteCopiedNodes(), Is.True);
        Assert.That(vm.SelectedSamples.Intersect(copies), Is.Empty);
        vm.UndoCommand.Execute(null);
        Assert.That(first.Samples.Count, Is.EqualTo(3));
        Assert.That(second.Samples.Count, Is.EqualTo(2));
        vm.RedoCommand.Execute(null);
        Assert.That(first.Samples.Count, Is.EqualTo(5));
    }

    [TestCase("tail")]
    [TestCase("track")]
    [TestCase("file")]
    [TestCase("overflow")]
    public async Task InvalidPasteNeverPartiallyChangesProject(string reason)
    {
        if (reason == "overflow") { a.Timestamp = int.MinValue; b.Timestamp = int.MaxValue; }
        vm.SetNodeSelection([a, b, c]); vm.CopySelectedNodes();
        if (reason == "tail") await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(9900));
        if (reason == "track") await vm.RemoveTrackCommand.ExecuteAsync(second);
        if (reason == "file") vm.SampleFiles.Remove("b.wav");
        var before = vm.Tracks.SelectMany(t => t.Samples).ToArray();
        var revision = vm.SamplesRevision;
        Assert.That(vm.PasteCopiedNodes(), Is.False);
        Assert.That(vm.Tracks.SelectMany(t => t.Samples), Is.EqualTo(before));
        Assert.That(vm.SamplesRevision, Is.EqualTo(revision));
        Assert.That(vm.ErrorMessage, Is.Not.Null.And.Not.Empty);
        Assert.That(vm.CanPasteNodes, Is.True);
    }

    [TestCase(260, 1250, 1500, 1375)]
    [TestCase(-260, 750, 1000, 875)]
    [TestCase(-10000, 0, 250, 125)]
    [TestCase(10000, 9625, 9875, 9750)]
    public async Task DragIsDraftUntilReleaseAndCommitsOneGroup(double delta, int x, int y, int z)
    {
        vm.SetNodeSelection([a, b, c]);
        var writes = saves.Writes; var scheduled = audio.SampleUpdates.Count; var revision = vm.SamplesRevision;
        Assert.That(vm.BeginNodeDrag(first, a), Is.True);
        vm.PreviewNodeDrag(delta);
        Assert.That(new[] { a.Timestamp, b.Timestamp, c.Timestamp }, Is.EqualTo(new[] { 1000, 1250, 1125 }));
        Assert.That(vm.NodePreviewTimestamps[a], Is.EqualTo(x));
        Assert.That(saves.Writes, Is.EqualTo(writes));
        Assert.That(audio.SampleUpdates.Count, Is.EqualTo(scheduled));
        Assert.That(vm.CommitNodeDrag(), Is.True);
        Assert.That(new[] { a.Timestamp, b.Timestamp, c.Timestamp }, Is.EqualTo(new[] { x, y, z }));
        Assert.That(vm.SamplesRevision, Is.EqualTo(revision + 1));
        await vm.FlushSavesAsync(); await vm.FlushPlaybackUpdatesAsync();
        Assert.That(saves.Writes, Is.EqualTo(writes + 1));
        Assert.That(audio.SampleUpdates.Count, Is.EqualTo(scheduled + 1));
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks[0].Samples[0].Timestamp, Is.EqualTo(x));
        vm.UndoCommand.Execute(null);
        Assert.That(new[] { a.Timestamp, b.Timestamp, c.Timestamp }, Is.EqualTo(new[] { 1000, 1250, 1125 }));
        vm.RedoCommand.Execute(null);
        Assert.That(a.Timestamp, Is.EqualTo(x));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancelOrZeroMovementLeavesHistoryAndRedoAlone(bool zero)
    {
        vm.PlaceSample(first, 500); vm.UndoCommand.Execute(null);
        vm.SetNodeSelection([a, c]); vm.BeginNodeDrag(first, a); vm.PreviewNodeDrag(zero ? 0 : 260);
        if (zero) Assert.That(vm.CommitNodeDrag(), Is.False);
        else vm.CancelNodeDrag();
        Assert.That(a.Timestamp, Is.EqualTo(1000));
        Assert.That(vm.CanRedo, Is.True);
        Assert.That(vm.NodePreviewTimestamps, Is.Empty);
    }

    [Test]
    public void StaleDragCannotCommitPartialMovement()
    {
        vm.SetNodeSelection([a, c]); vm.BeginNodeDrag(first, a); vm.PreviewNodeDrag(250);
        c.Timestamp = 999;
        Assert.That(vm.CommitNodeDrag(), Is.False);
        Assert.That(a.Timestamp, Is.EqualTo(1000));
        Assert.That(c.Timestamp, Is.EqualTo(999));
        Assert.That(vm.CanUndo, Is.False);
    }

    [Test]
    public void GroupDeletionRestoresIdentityAndIndexInOneStep()
    {
        vm.SetNodeSelection([a, b, c]);
        Assert.That(vm.RemoveSelectedNodes(), Is.True);
        Assert.That(first.Samples, Is.Empty); Assert.That(second.Samples, Is.Empty);
        Assert.That(vm.SelectedSamples, Is.Empty);
        vm.UndoCommand.Execute(null);
        Assert.That(first.Samples, Is.EqualTo(new[] { a, b }));
        Assert.That(second.Samples.Single(), Is.SameAs(c));
        vm.RedoCommand.Execute(null);
        Assert.That(first.Samples, Is.Empty);
    }

    [Test]
    public async Task ReloadAndDeactivationClearTransientEditing()
    {
        vm.SetNodeSelection([a, c]); vm.CopySelectedNodes(); vm.BeginNodeDrag(first, a); vm.PreviewNodeDrag(250);
        var oldGeneration = vm.LoadGeneration;
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        vm.RestoreNodeSelection([a, c], oldGeneration);
        Assert.That(vm.SelectedSamples, Is.Empty);
        Assert.That(vm.CanPasteNodes, Is.False);
        Assert.That(vm.IsNodeDragActive, Is.False);
        vm.SetNodeSelection([a]); vm.CopySelectedNodes();
        await vm.DeactivateAsync();
        Assert.That(vm.SelectedSamples, Is.Empty);
        Assert.That(vm.CanPasteNodes, Is.False);
        Assert.That(vm.CopySelectedNodes(), Is.False);
        Assert.That(vm.PasteCopiedNodes(), Is.False);
    }

    [Test]
    public void KeyboardMovesEarliestNodeWithoutDistortingFormation()
    {
        vm.SetNodeSelection([a, b, c]);
        Assert.That(vm.MoveSelectedNodes(true), Is.True);
        Assert.That(new[] { a.Timestamp, b.Timestamp, c.Timestamp }, Is.EqualTo(new[] { 1125, 1375, 1250 }));
        Assert.That(vm.MoveSelectedNodes(false), Is.True);
        Assert.That(a.Timestamp, Is.EqualTo(1000));
        vm.Project!.BPMs = [123]; a.Timestamp = 122; b.Timestamp = 366;
        vm.SetNodeSelection([a, b]);
        Assert.That(vm.MoveSelectedNodes(false), Is.True);
        Assert.That(new[] { a.Timestamp, b.Timestamp }, Is.EqualTo(new[] { 0, 244 }));
    }

    [Test]
    public void BusyReturnBlocksEditsAndCancelsDraft()
    {
        vm.SetNodeSelection([a, c]); vm.CopySelectedNodes(); vm.BeginNodeDrag(first, a); vm.PreviewNodeDrag(250);
        vm.IsReturning = true;
        Assert.That(vm.IsNodeDragActive, Is.False);
        Assert.That(vm.CanCopyNodes, Is.False);
        Assert.That(vm.PasteCopiedNodes(), Is.False);
        Assert.That(vm.RemoveSelectedNodes(), Is.False);
        Assert.That(a.Timestamp, Is.EqualTo(1000));
        vm.IsReturning = false;
    }
}
