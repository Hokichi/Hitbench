using Hitbench.Test;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.Services.ProjectFiles;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

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
        a = new Sample { Timestamp = 1000, FileName = "t1.wav" };
        b = new Sample { Timestamp = 1500, FileName = "t4.wav" };
        tracks[0].Samples.Add(a); tracks[3].Samples.Add(b);
        var data = new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]), Tracks = [.. tracks] };
        audio = new StubWorkstationAudioService();
        saves = new DelayedProjectFileService(); saves.Release();
        dialogs = new StubNodePasteDialogService();
        vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", data)) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(),
            new StubSampleFileService(tracks.Select(t => t.SelectedSampleFileName!).ToArray()),
            projectFiles: saves, nodePasteDialog: dialogs);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        vm.SetNodeSelection([a, b]);
        await vm.FlushSavesAsync(); await vm.FlushPlaybackUpdatesAsync();
    }

    [TearDown]
    public async Task Cleanup() { await vm.DeactivateAsync(); audio.Dispose(); }

    [TestCase(false, false, false, 0d)]
    [TestCase(false, true, true, 0d)]
    [TestCase(false, true, false, 1d)]
    [TestCase(true, false, false, 0d)]
    [TestCase(true, true, true, 0d)]
    [TestCase(true, true, false, 1d)]
    public async Task ExactOverlapCopyCancelsSilently(bool group, bool explicitDestination, bool previewAway, double finalDelta)
    {
        vm.SelectedSubdivisionIndex = 3;
        vm.SetNodeSelection(group ? [a, b] : [a]);
        vm.BeginNodeDrag(tracks[0], a);
        var target = explicitDestination ? tracks[0] : null;
        if (previewAway) vm.PreviewNodeDrag(250, target);
        Assert.That(vm.PreviewNodeDrag(finalDelta, target), Is.True);
        Assert.That(vm.NodePreviewTimestamps.All(pair => pair.Key.Timestamp == pair.Value), Is.True);
        vm.SetNodeDragCopy(true);
        var selection = vm.SelectedSamples.ToArray(); var error = vm.ErrorMessage;
        Assert.That(await vm.CommitNodeDragAsync(), Is.False);
        Assert.That(dialogs.Collisions, Is.Zero);
        Assert.That(vm.ErrorMessage, Is.EqualTo(error));
        AssertOriginalFormation();
        Assert.That(vm.SelectedSamples, Is.EquivalentTo(selection));
        Assert.That(vm.NodePreviewTimestamps, Is.Empty);
        Assert.That(vm.NodePreviewByTrack, Is.Empty);
        Assert.That(vm.IsNodeDragActive, Is.False);
        Assert.That(vm.IsNodeDragCopy, Is.False);
        Assert.That(vm.DragDestinationTrack, Is.Null);
    }

    [Test]
    public async Task SameTimestampCopyToDifferentTrackIsNotNoOp()
    {
        vm.SetNodeSelection([a]); vm.BeginNodeDrag(tracks[0], a);
        vm.PreviewNodeDrag(0, tracks[1]); vm.SetNodeDragCopy(true);
        Assert.That(await vm.CommitNodeDragAsync(), Is.True);
        Assert.That(tracks[1].Samples.Single(), Is.Not.SameAs(a));
        Assert.That(tracks[1].Samples.Single().Timestamp, Is.EqualTo(1000));
        Assert.That(tracks[0].Samples.Single(), Is.SameAs(a));
        Assert.That(dialogs.Collisions, Is.Zero);
    }

    [Test]
    public async Task NormalizedPartialOverlapStillCollides()
    {
        vm.SelectedSubdivisionIndex = 3; a.Timestamp = 1001;
        vm.BeginNodeDrag(tracks[3], b); vm.PreviewNodeDrag(0, tracks[3]); vm.SetNodeDragCopy(true);
        Assert.That(await vm.CommitNodeDragAsync(), Is.False);
        Assert.That(dialogs.Collisions, Is.EqualTo(1));
        Assert.That(a.Timestamp, Is.EqualTo(1001)); Assert.That(b.Timestamp, Is.EqualTo(1500));
        Assert.That(tracks.Sum(t => t.Samples.Count), Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CopyDragPreservesOriginalsAndRecordsOnePlacementBatch(bool acrossTracks)
    {
        var writes = saves.Writes; var updates = audio.SampleUpdates.Count; var revision = vm.SamplesRevision;
        vm.BeginNodeDrag(tracks[3], b);
        vm.PreviewNodeDrag(250, tracks[acrossTracks ? 5 : 3]);
        vm.SetNodeDragCopy(true);
        Assert.That(vm.IsNodeDragCopy, Is.True);
        Assert.That(await vm.CommitNodeDragAsync(), Is.True);
        Assert.That(tracks[0].Samples.Contains(a), Is.True); Assert.That(tracks[3].Samples.Contains(b), Is.True);
        Assert.That((a.Timestamp, b.Timestamp), Is.EqualTo((1000, 1500)));
        var copies = vm.SelectedSamples.OrderBy(n => n.Timestamp).ToArray();
        Assert.That(copies, Has.Length.EqualTo(2));
        Assert.That(copies.Intersect(new[] { a, b }), Is.Empty);
        Assert.That(copies.Select(n => n.Timestamp), Is.EqualTo(new[] { 1250, 1750 }));
        Assert.That(tracks[acrossTracks ? 2 : 0].Samples.Contains(copies[0]), Is.True);
        Assert.That(tracks[acrossTracks ? 5 : 3].Samples.Contains(copies[1]), Is.True);
        Assert.That(copies.Select(n => n.FileName), Is.EqualTo(acrossTracks ? new[] { "t3.wav", "t6.wav" } : new[] { "t1.wav", "t4.wav" }));
        Assert.That(vm.SamplesRevision, Is.EqualTo(revision + 1));
        Assert.That(vm.IsNodeDragCopy, Is.False);
        await vm.FlushSavesAsync(); await vm.FlushPlaybackUpdatesAsync();
        Assert.That(saves.Writes, Is.EqualTo(writes + 1)); Assert.That(audio.SampleUpdates.Count, Is.EqualTo(updates + 1));
        vm.UndoCommand.Execute(null); AssertOriginalFormation(); Assert.That(vm.CanUndo, Is.False);
        vm.RedoCommand.Execute(null); Assert.That(vm.SelectedSamples.Intersect(new[] { a, b }), Is.Empty);
        Assert.That(tracks.SelectMany(t => t.Samples).Intersect(copies).ToArray(), Has.Length.EqualTo(2));
    }

    [TestCase("zero")]
    [TestCase("original")]
    [TestCase("occupied")]
    [TestCase("file")]
    [TestCase("cancel")]
    [TestCase("invalid")]
    public async Task CopyDragRejectsWholeGroupWithoutChangingOriginals(string reason)
    {
        vm.BeginNodeDrag(tracks[3], b);
        vm.PreviewNodeDrag(reason == "zero" ? 0 : 250, tracks[3]);
        vm.SetNodeDragCopy(true);
        if (reason == "original")
        {
            var other = new Sample { Timestamp = 1250, FileName = "t1.wav" }; tracks[0].Samples.Add(other);
            vm.SetNodeSelection([a, other]); vm.BeginNodeDrag(tracks[0], a); vm.PreviewNodeDrag(250, tracks[0]); vm.SetNodeDragCopy(true);
        }
        if (reason == "occupied") tracks[0].Samples.Add(new Sample { Timestamp = 1250, FileName = "t1.wav" });
        if (reason == "file") vm.SampleFiles.Remove("t1.wav");
        if (reason == "cancel") vm.CancelNodeDrag();
        if (reason == "invalid") vm.PreviewNodeDrag(250, tracks[0]);
        var before = tracks.Select(t => t.Samples.ToArray()).ToArray();
        Assert.That(await vm.CommitNodeDragAsync(), Is.False);
        Assert.That(dialogs.Collisions, Is.EqualTo(reason is "original" or "occupied" ? 1 : 0));
        for (var i = 0; i < tracks.Length; i++) Assert.That(tracks[i].Samples, Is.EqualTo(before[i]));
        Assert.That(vm.CanUndo, Is.False); Assert.That(vm.IsNodeDragActive, Is.False);
        Assert.That(vm.IsNodeDragCopy, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LastCopyModeWinsWithoutChangingDraftGeometry(bool copy)
    {
        vm.BeginNodeDrag(tracks[3], b); vm.PreviewNodeDrag(250, tracks[5]);
        var preview = vm.NodePreviewByTrack;
        vm.SetNodeDragCopy(true); vm.SetNodeDragCopy(false); vm.SetNodeDragCopy(copy);
        Assert.That(vm.NodePreviewByTrack, Is.SameAs(preview));
        Assert.That(await vm.CommitNodeDragAsync(), Is.True);
        Assert.That(tracks[0].Samples.Contains(a), Is.EqualTo(copy));
        Assert.That(a.Timestamp, Is.EqualTo(copy ? 1000 : 1250));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CopyCollisionCannotMutateAfterLifecycleChange(bool deactivate)
    {
        var pending = new TaskCompletionSource(); dialogs.Collision = token => pending.Task.WaitAsync(token);
        vm.SetNodeSelection([a]); vm.BeginNodeDrag(tracks[0], a); vm.PreviewNodeDrag(500, tracks[3]); vm.SetNodeDragCopy(true);
        var operation = vm.CommitNodeDragAsync();
        Assert.That(vm.IsNodeEditBusy, Is.True);
        Assert.That(dialogs.Collisions, Is.EqualTo(1));
        if (deactivate) await vm.DeactivateAsync();
        else { await vm.LoadAsync("other.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true; }
        pending.TrySetResult(); Assert.That(await operation, Is.False);
        AssertOriginalFormation(); Assert.That(vm.CanUndo, Is.False);
    }

    [Test]
    public async Task PreviewGroupsEveryNodeByMappedTrack()
    {
        var c = new Sample { Timestamp = 1250, FileName = "t1.wav" }; tracks[0].Samples.Add(c);
        vm.SetNodeSelection([a, c, b]);
        var revision = vm.SamplesRevision; var writes = saves.Writes; var updates = audio.SampleUpdates.Count;
        vm.BeginNodeDrag(tracks[3], b);
        Assert.That(vm.PreviewNodeDrag(250, tracks[5]), Is.True);
        Assert.That(vm.NodePreviewByTrack[tracks[2]], Is.EqualTo(new[] { 1250, 1500 }));
        Assert.That(vm.NodePreviewByTrack[tracks[5]], Is.EqualTo(new[] { 1750 }));
        Assert.That(vm.NodePreviewByTrack.Values.Sum(nodes => nodes.Count), Is.EqualTo(3));
        Assert.That(new[] { a.Timestamp, c.Timestamp, b.Timestamp }, Is.EqualTo(new[] { 1000, 1250, 1500 }));
        await vm.FlushSavesAsync(); await vm.FlushPlaybackUpdatesAsync();
        Assert.That(vm.SamplesRevision, Is.EqualTo(revision)); Assert.That(vm.CanUndo, Is.False);
        Assert.That(saves.Writes, Is.EqualTo(writes)); Assert.That(audio.SampleUpdates, Has.Count.EqualTo(updates));
    }

    [Test]
    public void SameTimestampPreviewKeepsDifferentTracks()
    {
        b.Timestamp = a.Timestamp; vm.BeginNodeDrag(tracks[3], b); vm.PreviewNodeDrag(250, tracks[5]);
        Assert.That(vm.NodePreviewByTrack, Has.Count.EqualTo(2));
        Assert.That(vm.NodePreviewByTrack[tracks[2]], Is.EqualTo(new[] { 1250 }));
        Assert.That(vm.NodePreviewByTrack[tracks[5]], Is.EqualTo(new[] { 1250 }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ClearingPreviewKeepsGestureAndRejectsStaleCommit(bool recover)
    {
        vm.BeginNodeDrag(tracks[3], b); vm.PreviewNodeDrag(250, tracks[5]);
        vm.ClearNodeDragPreview();
        Assert.That(vm.NodePreviewByTrack, Is.Empty); Assert.That(vm.NodePreviewTimestamps, Is.Empty);
        Assert.That(vm.IsNodeDragActive, Is.True);
        if (recover) Assert.That(vm.PreviewNodeDrag(250, tracks[5]), Is.True);
        Assert.That(await vm.CommitNodeDragAsync(), Is.EqualTo(recover));
        if (recover) { Assert.That(tracks[2].Samples.Single(), Is.SameAs(a)); Assert.That(tracks[5].Samples.Single(), Is.SameAs(b)); }
        else AssertOriginalFormation();
        Assert.That(vm.NodePreviewByTrack, Is.Empty); Assert.That(vm.NodePreviewTimestamps, Is.Empty);
    }

    [TestCase("invalid")]
    [TestCase("cancel")]
    [TestCase("reload")]
    [TestCase("deactivate")]
    public async Task InvalidTargetAndCancelClearEveryPreview(string action)
    {
        vm.BeginNodeDrag(tracks[3], b); vm.PreviewNodeDrag(250, tracks[5]);
        switch (action)
        {
            case "invalid": Assert.That(vm.PreviewNodeDrag(250, tracks[0]), Is.False); break;
            case "cancel": vm.CancelNodeDrag(); break;
            case "reload": await vm.LoadAsync("project.json", CancellationToken.None); break;
            case "deactivate": await vm.DeactivateAsync(); break;
        }
        Assert.That(vm.NodePreviewByTrack, Is.Empty); Assert.That(vm.NodePreviewTimestamps, Is.Empty);
    }

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

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExactOverlapCopyPreservesRedoAndSideEffects(bool followMarker)
    {
        Assert.That(vm.PlaceSample(tracks[0], 500), Is.True);
        var undone = tracks[0].Samples.Single(n => n.Timestamp == 500);
        vm.UndoCommand.Execute(null); vm.SetNodeSelection([a, b]);
        await vm.FlushSavesAsync(); await vm.FlushPlaybackUpdatesAsync();
        vm.BeginNodeDrag(tracks[0], a); vm.PreviewNodeDrag(0, tracks[0]); vm.SetNodeDragCopy(true);
        vm.ScrollWithMarker = followMarker;
        var revision = vm.SamplesRevision; var writes = saves.Writes; var updates = audio.SampleUpdates.Count;
        var error = vm.ErrorMessage; var publications = 0;
        System.ComponentModel.PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName == nameof(vm.SamplesRevision)) publications++; };
        vm.PropertyChanged += changed;
        try
        {
            Assert.That(await vm.CommitNodeDragAsync(), Is.False);
            await vm.FlushSavesAsync(); await vm.FlushPlaybackUpdatesAsync();
            AssertOriginalFormation();
            Assert.That(vm.SelectedSamples, Is.EquivalentTo(new[] { a, b }));
            Assert.That(vm.ErrorMessage, Is.EqualTo(error));
            Assert.That(vm.CanUndo, Is.False); Assert.That(vm.CanRedo, Is.True);
            Assert.That(vm.ScrollWithMarker, Is.EqualTo(followMarker));
            Assert.That(vm.SamplesRevision, Is.EqualTo(revision));
            Assert.That(saves.Writes, Is.EqualTo(writes));
            Assert.That(audio.SampleUpdates, Has.Count.EqualTo(updates));
            Assert.That(dialogs.Collisions, Is.Zero);
            Assert.That(publications, Is.Zero);
        }
        finally
        {
            vm.PropertyChanged -= changed;
        }
        vm.RedoCommand.Execute(null); Assert.That(tracks[0].Samples.Contains(undone), Is.True);
        vm.UndoCommand.Execute(null); AssertOriginalFormation();
    }

    [Test]
    public async Task ExactOverlapCopyIgnoresUnavailableDestinationFile()
    {
        vm.BeginNodeDrag(tracks[0], a); vm.PreviewNodeDrag(0, tracks[0]); vm.SetNodeDragCopy(true);
        vm.SampleFiles.Remove("t1.wav");
        var error = vm.ErrorMessage; var revision = vm.SamplesRevision;
        var writes = saves.Writes; var updates = audio.SampleUpdates.Count;
        Assert.That(await vm.CommitNodeDragAsync(), Is.False);
        await vm.FlushSavesAsync(); await vm.FlushPlaybackUpdatesAsync();
        AssertOriginalFormation();
        Assert.That(vm.SelectedSamples, Is.EquivalentTo(new[] { a, b }));
        Assert.That(vm.CanUndo, Is.False); Assert.That(vm.CanRedo, Is.False);
        Assert.That(vm.ErrorMessage, Is.EqualTo(error)); Assert.That(dialogs.Collisions, Is.Zero);
        Assert.That(vm.SamplesRevision, Is.EqualTo(revision)); Assert.That(saves.Writes, Is.EqualTo(writes));
        Assert.That(audio.SampleUpdates, Has.Count.EqualTo(updates));
    }

    [TestCase("membership")]
    [TestCase("timestamp")]
    [TestCase("reorder")]
    [TestCase("reload")]
    [TestCase("deactivate")]
    [TestCase("invalid")]
    public async Task StaleExactOverlapCopyCancelsSafely(string reason)
    {
        vm.BeginNodeDrag(tracks[0], a); vm.PreviewNodeDrag(0, tracks[0]); vm.SetNodeDragCopy(true);
        switch (reason)
        {
            case "membership": tracks[0].Samples.Remove(a); break;
            case "timestamp": a.Timestamp = 999; break;
            case "reorder": vm.Tracks.Move(1, 2); break;
            case "reload": await vm.LoadAsync("other.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); break;
            case "deactivate": await vm.DeactivateAsync(); break;
            case "invalid": vm.PreviewNodeDrag(double.NaN); break;
        }
        await vm.FlushSavesAsync(); await vm.FlushPlaybackUpdatesAsync();
        var contents = tracks.Select(t => t.Samples.ToArray()).ToArray();
        var revision = vm.SamplesRevision; var writes = saves.Writes; var updates = audio.SampleUpdates.Count;
        var error = vm.ErrorMessage;
        Assert.That(await vm.CommitNodeDragAsync(), Is.False);
        await vm.FlushSavesAsync(); await vm.FlushPlaybackUpdatesAsync();
        for (var i = 0; i < tracks.Length; i++) Assert.That(tracks[i].Samples, Is.EqualTo(contents[i]));
        Assert.That(a.Timestamp, Is.EqualTo(reason == "timestamp" ? 999 : 1000));
        Assert.That(vm.ErrorMessage, Is.EqualTo(error)); Assert.That(dialogs.Collisions, Is.Zero);
        Assert.That(vm.NodePreviewTimestamps, Is.Empty); Assert.That(vm.NodePreviewByTrack, Is.Empty);
        Assert.That(vm.IsNodeDragActive, Is.False); Assert.That(vm.IsNodeDragCopy, Is.False);
        Assert.That(vm.SamplesRevision, Is.EqualTo(revision)); Assert.That(saves.Writes, Is.EqualTo(writes));
        Assert.That(audio.SampleUpdates, Has.Count.EqualTo(updates));
    }

    private void AssertOriginalFormation()
    {
        Assert.That(tracks[0].Samples.Single(), Is.SameAs(a));
        Assert.That(tracks[3].Samples.Single(), Is.SameAs(b));
        Assert.That(new[] { a.Timestamp, b.Timestamp }, Is.EqualTo(new[] { 1000, 1500 }));
        Assert.That(new[] { a.FileName, b.FileName }, Is.EqualTo(new[] { "t1.wav", "t4.wav" }));
    }
}
