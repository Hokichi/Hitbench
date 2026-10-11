using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class WorkstationTrackAuditionTests
{
    [Test]
    public async Task MuteSuppressesOnlyItsTrackAndRestoresStablePlacementIds()
    {
        var a = TrackWithSample("a.wav"); var b = TrackWithSample("b.wav");
        var (vm, audio, saves) = await CreateReadyAsync(a, b);
        var original = audio.SampleUpdates.Last().Select(e => e.Id).ToArray();
        vm.SetTrackMuted(a, true);
        Assert.That(audio.SampleUpdates.Last().Select(e => e.FileName), Is.EqualTo(new[] { "b.wav" }));
        Assert.That(a.IsTrackMuted, Is.True);
        vm.SetTrackMuted(a, false);
        await vm.FlushSavesAsync();
        Assert.That(audio.SampleUpdates.Last().Select(e => e.Id), Is.EqualTo(original));
        Assert.That(audio.SampleUpdates.Last().Select(e => e.TrackVolume), Is.EqualTo(new[] { 70, 70 }));
        Assert.That(saves.Snapshots.Select(json => ProjectDataSerializer.Deserialize(json).Tracks[0].IsTrackMuted),
            Is.EqualTo(new[] { true, false }));
        Assert.That(vm.CanUndo, Is.False);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.Zero));
        Assert.That(audio.SongVolume, Is.EqualTo(100));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task SoloIncludesAllSoloedTracksAndMuteWins()
    {
        var a = TrackWithSample("a.wav"); var b = TrackWithSample("b.wav");
        var (vm, audio, _) = await CreateReadyAsync(a, b);
        vm.SetTrackSoloed(a, true);
        Assert.That(audio.SampleUpdates.Last().Select(e => e.FileName), Is.EqualTo(new[] { "a.wav" }));
        vm.SetTrackSoloed(b, true);
        Assert.That(audio.SampleUpdates.Last(), Has.Count.EqualTo(2));
        vm.SetTrackMuted(a, true);
        Assert.That(audio.SampleUpdates.Last().Select(e => e.FileName), Is.EqualTo(new[] { "b.wav" }));
        vm.SetTrackSoloed(a, false); vm.SetTrackSoloed(b, false); vm.SetTrackMuted(a, false);
        Assert.That(audio.SampleUpdates.Last(), Has.Count.EqualTo(2));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task AuditionRejectsUnreadyForeignBusyAndDeactivatedTracks()
    {
        var track = TrackWithSample("a.wav"); var stranger = new Track();
        var audio = new StubWorkstationAudioService(); var saves = new DelayedProjectFileService(); saves.Release();
        var confirmation = new TaskCompletionSource<bool>();
        var dialog = new StubTrackRemovalDialogService { Confirm = _ => confirmation.Task };
        var vm = CreateVM(audio, saves, [track], dialog);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.SetTrackMuted(track, true); vm.SetTrackSoloed(track, true);
        Assert.That(track.IsTrackMuted || track.IsTrackSoloed, Is.False);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        vm.SetTrackMuted(stranger, true); vm.SetTrackSoloed(stranger, true);
        Assert.That(stranger.IsTrackMuted || stranger.IsTrackSoloed, Is.False);
        var removal = vm.RemoveTrackCommand.ExecuteAsync(track);
        vm.SetTrackMuted(track, true); vm.SetTrackSoloed(track, true);
        Assert.That(track.IsTrackMuted || track.IsTrackSoloed, Is.False);
        confirmation.SetResult(false); await removal;
        vm.SetTrackMuted(track, true);
        var revision = vm.TrackAuditionRevision; var updates = audio.SampleUpdates.Count;
        vm.SetTrackMuted(track, true);
        Assert.That(vm.TrackAuditionRevision, Is.EqualTo(revision));
        Assert.That(audio.SampleUpdates.Count, Is.EqualTo(updates));
        await vm.DeactivateAsync();
        vm.SetTrackMuted(track, false); vm.SetTrackSoloed(track, true);
        Assert.That(track.IsTrackMuted, Is.True); Assert.That(track.IsTrackSoloed, Is.False);
        Assert.That(saves.Writes, Is.EqualTo(1));
    }

    [Test]
    public async Task SavedFlagsAffectInitialPlaybackAndReloadWithoutInitializationSaves()
    {
        var a = TrackWithSample("a.wav"); a.IsTrackMuted = true; a.IsTrackSoloed = true;
        var b = TrackWithSample("b.wav"); b.IsTrackSoloed = true;
        var project = Project([a, b]); var json = ProjectDataSerializer.Serialize(project);
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", ProjectDataSerializer.Deserialize(json))) };
        var audio = new StubWorkstationAudioService(); var saves = new DelayedProjectFileService(); saves.Release();
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), projectFiles: saves);
        await vm.LoadAsync("project.json", CancellationToken.None);
        Assert.That(audio.SampleUpdates.First().Select(e => e.FileName), Is.EqualTo(new[] { "b.wav" }));
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        var old = vm.Tracks[0];
        await vm.LoadAsync("second.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        vm.SetTrackMuted(old, false);
        Assert.That(old.IsTrackMuted, Is.True);
        Assert.That(vm.IsTrackMuted(vm.Tracks[0]), Is.True);
        Assert.That(vm.HasMutedTracks && vm.HasSoloedTracks, Is.True);
        Assert.That(vm.IsTrackSoloed(vm.Tracks[1]), Is.True);
        Assert.That(audio.SampleUpdates.Last().Select(e => e.FileName), Is.EqualTo(new[] { "b.wav" }));
        Assert.That(saves.Writes, Is.Zero);
        await vm.DeactivateAsync();
        Assert.That(old.IsTrackSoloed, Is.True);
    }

    [Test]
    public async Task RemovingLastSoloedTrackRestoresSiblingPlaybackAndSavesRemainingFlags()
    {
        var a = TrackWithSample("a.wav"); a.IsTrackSoloed = true;
        var b = TrackWithSample("b.wav");
        var audio = new StubWorkstationAudioService(); var saves = new DelayedProjectFileService(); saves.Release();
        var vm = CreateVM(audio, saves, [a, b], new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) });
        await vm.LoadAsync("project.json", CancellationToken.None); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        await vm.RemoveTrackCommand.ExecuteAsync(a); await vm.FlushSavesAsync();
        Assert.That(audio.SampleUpdates.Last().Select(e => e.FileName), Is.EqualTo(new[] { "b.wav" }));
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks.Single().IsTrackSoloed, Is.False);
        Assert.That(a.IsTrackSoloed, Is.True);
        Assert.That(vm.HasSoloedTracks, Is.False);
        Assert.That(vm.ClearTrackSolosCommand.CanExecute(null), Is.False);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task AuditionSaveFailureRetainsFlagsAndRetryPersistsLatestValues()
    {
        var track = TrackWithSample("a.wav"); var saves = new FailOnceProjectFileService();
        var vm = CreateVM(new StubWorkstationAudioService(), saves, [track]);
        await vm.LoadAsync("project.json", CancellationToken.None); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        vm.SetTrackMuted(track, true);
        Assert.ThrowsAsync<IOException>(() => vm.FlushSavesAsync());
        Assert.That(vm.SaveError, Is.Not.Null); Assert.That(track.IsTrackMuted, Is.True);
        vm.SetTrackSoloed(track, true); vm.RetrySaveCommand.Execute(null); await vm.FlushSavesAsync();
        var restored = ProjectDataSerializer.Deserialize(saves.SavedJson!).Tracks.Single();
        Assert.That(restored.IsTrackMuted && restored.IsTrackSoloed, Is.True);
        Assert.That(vm.SaveError, Is.Null);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task RapidAuditionChangesCancelEarlierPreparationAndKeepLatestState()
    {
        var a = TrackWithSample("a.wav"); var b = TrackWithSample("b.wav");
        var (vm, audio, _) = await CreateReadyAsync(a, b);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new List<CancellationToken>();
        audio.PrepareSamplesAsync = token => { tokens.Add(token); return release.Task.WaitAsync(token); };
        vm.SetTrackMuted(a, true); vm.SetTrackSoloed(b, true); vm.SetTrackMuted(a, false);
        Assert.That(tokens.Take(2).All(token => token.IsCancellationRequested), Is.True);
        release.SetResult(); await vm.PlayPauseCommand.ExecuteAsync(null);
        Assert.That(audio.SampleUpdates.Last().Select(e => e.FileName), Is.EqualTo(new[] { "b.wav" }));
        Assert.That(vm.ErrorMessage, Is.Null);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task AuditionRejectsPendingImportAndPreservesFlagsDuringSampleRefresh()
    {
        var track = TrackWithSample("a.wav"); track.IsTrackMuted = true; track.IsTrackSoloed = true;
        var selection = new TaskCompletionSource<IReadOnlyList<string>>();
        var dialog = new StubSampleFileDialogService(Array.Empty<string>()) { Selection = _ => selection.Task };
        var files = new StubSampleFileService(["a.wav", "b.wav"]);
        var saves = new DelayedProjectFileService(); saves.Release();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", Project([track]))) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            files, dialog, projectFiles: saves);
        await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        var import = vm.ImportSamplesAsync();
        vm.SetTrackMuted(track, false); vm.SetTrackSoloed(track, false);
        Assert.That(track.IsTrackMuted && track.IsTrackSoloed, Is.True);
        selection.SetResult(["b.wav"]); await import;
        vm.SelectSample(track, "b.wav");
        Assert.That(track.IsTrackMuted && track.IsTrackSoloed, Is.True);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task AuditionRejectsPendingNodeEditWithoutClearingFlags()
    {
        var a = TrackWithSample("a.wav"); var b = TrackWithSample("b.wav"); b.IsTrackMuted = true;
        var choice = new TaskCompletionSource<NodePasteTiming?>();
        var dialog = new StubNodePasteDialogService { Choose = _ => choice.Task };
        var saves = new DelayedProjectFileService(); saves.Release();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", Project([a, b]))) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            new StubSampleFileService(["a.wav", "b.wav"]), projectFiles: saves, nodePasteDialog: dialog);
        await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        vm.SelectNode(a, a.Samples.Single(), false); vm.CopySelectedNodes(); vm.SelectedTrack = b;
        var paste = vm.PasteCopiedNodesAsync();
        Assert.That(vm.IsNodeEditBusy, Is.True);
        vm.SetTrackMuted(b, false); vm.SetTrackSoloed(b, true);
        Assert.That(b.IsTrackMuted, Is.True); Assert.That(b.IsTrackSoloed, Is.False);
        choice.SetResult(null); await paste;
        Assert.That(saves.Writes, Is.Zero);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task ClearFlagsBatchesUpdatesAndPreservesOtherGroup()
    {
        var a = TrackWithSample("a.wav"); var b = TrackWithSample("b.wav");
        a.IsTrackMuted = b.IsTrackMuted = true; a.IsTrackSoloed = true;
        var (vm, audio, saves) = await CreateReadyAsync(a, b);
        var updates = audio.SampleUpdates.Count; var revision = vm.TrackAuditionRevision;
        vm.ClearTrackMutesCommand.Execute(null); await vm.FlushSavesAsync();
        Assert.That(vm.HasMutedTracks, Is.False); Assert.That(vm.HasSoloedTracks, Is.True);
        Assert.That(audio.SampleUpdates.Count, Is.EqualTo(updates + 1));
        Assert.That(vm.TrackAuditionRevision, Is.EqualTo(revision + 1));
        Assert.That(saves.Writes, Is.EqualTo(1));
        vm.ClearTrackMutesCommand.Execute(null); await vm.FlushSavesAsync();
        Assert.That(saves.Writes, Is.EqualTo(1));
        vm.ClearTrackSolosCommand.Execute(null); await vm.FlushSavesAsync();
        Assert.That(vm.HasSoloedTracks, Is.False);
        Assert.That(vm.ClearTrackSolosCommand.CanExecute(null), Is.False);
        await vm.DeactivateAsync();
    }

    private static Track TrackWithSample(string name) => new() { Volume = 70, SelectedSampleFileName = name,
        Samples = [new Sample { FileName = name, Timestamp = 125, Volume = 80 }] };
    private static ProjectData Project(Track[] tracks) => new() { Tracks = tracks.ToList(), Offsets = [0], BPMs = [120] };
    private static WorkstationPageVM CreateVM(StubWorkstationAudioService audio, IProjectFileService saves, Track[] tracks,
        StubTrackRemovalDialogService? dialog = null) => new(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", Project(tracks))) }, new MarkerPlaybackCoordinator(audio, Task.Delay),
            new ImmediateUiScheduler(), projectFiles: saves, trackRemovalDialog: dialog);
    private static async Task<(WorkstationPageVM Vm, StubWorkstationAudioService Audio, DelayedProjectFileService Saves)> CreateReadyAsync(params Track[] tracks)
    {
        var audio = new StubWorkstationAudioService(); var saves = new DelayedProjectFileService(); saves.Release();
        var vm = CreateVM(audio, saves, tracks);
        await vm.LoadAsync("project.json", CancellationToken.None); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        return (vm, audio, saves);
    }
}
