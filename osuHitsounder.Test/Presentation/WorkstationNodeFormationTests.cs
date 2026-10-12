using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class WorkstationNodeFormationTests
{
    private WorkstationPageVM vm = null!;
    private Track[] tracks = null!;
    private Sample a = null!, b = null!;
    private StubWorkstationAudioService audio = null!;
    private DelayedProjectFileService saves = null!;
    private StubNodePasteDialogService dialogs = null!;

    [SetUp]
    public async Task Setup()
    {
        tracks = Enumerable.Range(1, 6).Select(i => new Track { SelectedSampleFileName = $"t{i}.wav" }).ToArray();
        a = new Sample { Timestamp = 1000, FileName = "t1.wav", Volume = 37 };
        b = new Sample { Timestamp = 1500, FileName = "t4.wav", Volume = 64 };
        tracks[0].Samples.Add(a); tracks[3].Samples.Add(b);
        var data = new ProjectData { Offsets = [0], BPMs = [120], Tracks = [.. tracks] };
        audio = new StubWorkstationAudioService();
        saves = new DelayedProjectFileService(); saves.Release();
        dialogs = new StubNodePasteDialogService();
        vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", data)) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(),
            new StubSampleFileService(tracks.Select(t => t.SelectedSampleFileName!).ToArray()),
            projectFiles: saves, nodePasteDialog: dialogs);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        vm.SetNodeSelection([a, b]);
        await vm.FlushSavesAsync(); await vm.FlushPlaybackUpdatesAsync();
    }

    [TearDown]
    public async Task Cleanup() { await vm.DeactivateAsync(); audio.Dispose(); }

    [Test]
    public async Task DraggingLaterTrackPreservesFormation()
    {
        var revision = vm.SamplesRevision; var writes = saves.Writes; var updates = audio.SampleUpdates.Count;
        Assert.That(vm.BeginNodeDrag(tracks[3], b), Is.True);
        Assert.That(vm.PreviewNodeDrag(250, tracks[5]), Is.True);
        Assert.That(tracks[0].Samples.Single(), Is.SameAs(a));
        Assert.That(new[] { a.Timestamp, b.Timestamp }, Is.EqualTo(new[] { 1000, 1500 }));
        Assert.That(await vm.CommitNodeDragAsync(), Is.True);
        Assert.That(tracks[2].Samples.Single(), Is.SameAs(a));
        Assert.That(tracks[5].Samples.Single(), Is.SameAs(b));
        Assert.That(new[] { a.Timestamp, b.Timestamp }, Is.EqualTo(new[] { 1250, 1750 }));
        Assert.That(new[] { a.FileName, b.FileName }, Is.EqualTo(new[] { "t3.wav", "t6.wav" }));
        Assert.That(new[] { a.Volume, b.Volume }, Is.EqualTo(new[] { 37, 64 }));
        Assert.That(vm.SelectedSamples, Is.EquivalentTo(new[] { a, b }));
        Assert.That(vm.SelectedTrack, Is.SameAs(tracks[5]));
        Assert.That(vm.SamplesRevision, Is.EqualTo(revision + 1));
        await vm.FlushSavesAsync(); await vm.FlushPlaybackUpdatesAsync();
        Assert.That(saves.Writes, Is.EqualTo(writes + 1));
        Assert.That(audio.SampleUpdates, Has.Count.EqualTo(updates + 1));
        var saved = ProjectDataSerializer.Deserialize(saves.LatestJson!);
        Assert.That(saved.Tracks[2].Samples.Single().Timestamp, Is.EqualTo(1250));
        Assert.That(saved.Tracks[5].Samples.Single().FileName, Is.EqualTo("t6.wav"));
        vm.UndoCommand.Execute(null);
        AssertOriginalFormation(); Assert.That(vm.CanUndo, Is.False);
        vm.RedoCommand.Execute(null);
        Assert.That(tracks[2].Samples.Single(), Is.SameAs(a));
        Assert.That(tracks[5].Samples.Single(), Is.SameAs(b));
        Assert.That(new[] { a.Timestamp, b.Timestamp }, Is.EqualTo(new[] { 1250, 1750 }));
        Assert.That(new[] { a.FileName, b.FileName }, Is.EqualTo(new[] { "t3.wav", "t6.wav" }));
    }

    [Test]
    public async Task ZeroTrackShiftKeepsOriginalMembership()
    {
        vm.BeginNodeDrag(tracks[3], b); vm.PreviewNodeDrag(250, tracks[3]);
        Assert.That(await vm.CommitNodeDragAsync(), Is.True);
        Assert.That(tracks[0].Samples.Single(), Is.SameAs(a));
        Assert.That(tracks[3].Samples.Single(), Is.SameAs(b));
        Assert.That(new[] { a.Timestamp, b.Timestamp }, Is.EqualTo(new[] { 1250, 1750 }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task BoundaryRejectsEntireGroup(bool downward)
    {
        vm.PlaceSample(tracks[0], 500); vm.UndoCommand.Execute(null); vm.SetNodeSelection([a, b]);
        await vm.FlushSavesAsync(); await vm.FlushPlaybackUpdatesAsync();
        var revision = vm.SamplesRevision; var writes = saves.Writes; var updates = audio.SampleUpdates.Count;
        vm.BeginNodeDrag(downward ? tracks[0] : tracks[3], downward ? a : b);
        Assert.That(vm.PreviewNodeDrag(250, tracks[downward ? 5 : 0]), Is.False);
        Assert.That(await vm.CommitNodeDragAsync(), Is.False);
        AssertOriginalFormation(); Assert.That(vm.CanUndo, Is.False); Assert.That(vm.CanRedo, Is.True);
        await vm.FlushSavesAsync(); await vm.FlushPlaybackUpdatesAsync();
        Assert.That(vm.SamplesRevision, Is.EqualTo(revision)); Assert.That(saves.Writes, Is.EqualTo(writes));
        Assert.That(audio.SampleUpdates, Has.Count.EqualTo(updates));
    }

    [Test]
    public async Task SameTimestampOnDifferentTracksIsNotCollision()
    {
        b.Timestamp = a.Timestamp;
        vm.BeginNodeDrag(tracks[3], b); vm.PreviewNodeDrag(250, tracks[5]);
        Assert.That(await vm.CommitNodeDragAsync(), Is.True);
        Assert.That(tracks[2].Samples.Single(), Is.SameAs(a));
        Assert.That(tracks[5].Samples.Single(), Is.SameAs(b));
        Assert.That(dialogs.Collisions, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OverlappingSourceAndDestinationTracksUndoAsOneBatch(bool forward)
    {
        tracks[0].Samples.Clear(); tracks[3].Samples.Clear();
        a.FileName = "t2.wav"; b.FileName = "t3.wav";
        var other = new Sample { Timestamp = 1250, FileName = "t2.wav" };
        tracks[1].Samples.AddRange([a, other]); tracks[2].Samples.Add(b);
        vm.SetNodeSelection([a, other, b]);
        vm.BeginNodeDrag(tracks[1], a); vm.PreviewNodeDrag(0, tracks[forward ? 2 : 0]);
        Assert.That(await vm.CommitNodeDragAsync(), Is.True);
        var nextA = tracks[forward ? 2 : 0]; var nextB = tracks[forward ? 3 : 1];
        Assert.That(nextA.Samples, Is.EqualTo(new[] { a, other }));
        Assert.That(nextB.Samples.Single(), Is.SameAs(b));
        vm.UndoCommand.Execute(null);
        Assert.That(tracks[1].Samples, Is.EqualTo(new[] { a, other }));
        Assert.That(tracks[2].Samples.Single(), Is.SameAs(b));
        Assert.That(a.FileName, Is.EqualTo("t2.wav")); Assert.That(b.FileName, Is.EqualTo("t3.wav"));
        vm.RedoCommand.Execute(null);
        Assert.That(nextA.Samples, Is.EqualTo(new[] { a, other })); Assert.That(nextB.Samples.Single(), Is.SameAs(b));
    }

    [TestCase("collision")]
    [TestCase("file")]
    [TestCase("timestamp")]
    [TestCase("membership")]
    [TestCase("remove")]
    [TestCase("reorder")]
    public async Task InvalidMappedDestinationRejectsWholeGroup(string reason)
    {
        vm.BeginNodeDrag(tracks[3], b); vm.PreviewNodeDrag(250, tracks[5]);
        switch (reason)
        {
            case "collision": tracks[2].Samples.Add(new Sample { Timestamp = 1250, FileName = "t3.wav" }); break;
            case "file": vm.SampleFiles.Remove("t3.wav"); break;
            case "timestamp": b.Timestamp = 999; break;
            case "membership": tracks[3].Samples.Remove(b); break;
            case "remove": vm.Tracks.Remove(tracks[1]); break;
            case "reorder": vm.Tracks.Move(1, 2); break;
        }
        var before = tracks.Select(t => t.Samples.ToArray()).ToArray();
        var revision = vm.SamplesRevision;
        Assert.That(await vm.CommitNodeDragAsync(), Is.False);
        for (var i = 0; i < tracks.Length; i++) Assert.That(tracks[i].Samples, Is.EqualTo(before[i]));
        Assert.That(a.Timestamp, Is.EqualTo(1000)); Assert.That(b.Timestamp, Is.EqualTo(reason == "timestamp" ? 999 : 1500));
        Assert.That(vm.SamplesRevision, Is.EqualTo(revision)); Assert.That(vm.CanUndo, Is.False);
        Assert.That(dialogs.Collisions, Is.EqualTo(reason == "collision" ? 1 : 0));
    }

    [Test]
    public async Task KeyboardTransferPreservesFormationAndRejectsBoundary()
    {
        Assert.That(await vm.TransferSelectedNodesAsync(false), Is.False);
        AssertOriginalFormation();
        Assert.That(await vm.TransferSelectedNodesAsync(true), Is.True);
        Assert.That(tracks[1].Samples.Single(), Is.SameAs(a)); Assert.That(tracks[4].Samples.Single(), Is.SameAs(b));
        Assert.That(await vm.TransferSelectedNodesAsync(true), Is.True);
        Assert.That(tracks[2].Samples.Single(), Is.SameAs(a)); Assert.That(tracks[5].Samples.Single(), Is.SameAs(b));
        var revision = vm.SamplesRevision;
        Assert.That(await vm.TransferSelectedNodesAsync(true), Is.False);
        Assert.That(vm.SamplesRevision, Is.EqualTo(revision));
    }

    private void AssertOriginalFormation()
    {
        Assert.That(tracks[0].Samples.Single(), Is.SameAs(a));
        Assert.That(tracks[3].Samples.Single(), Is.SameAs(b));
        Assert.That(new[] { a.Timestamp, b.Timestamp }, Is.EqualTo(new[] { 1000, 1500 }));
        Assert.That(new[] { a.FileName, b.FileName }, Is.EqualTo(new[] { "t1.wav", "t4.wav" }));
    }
}
