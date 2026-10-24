using Onset.Core.Entities;
using Onset.Resources.DataModels;
using Onset.Services;
using Onset.Services.ProjectFiles;
using Onset.ViewModels.Shell;

namespace Onset.Test.Presentation;

public class WorkstationSampleReloadTests
{
    private static WorkstationPageVM Create(StubWorkstationSourceService source, StubSampleFileService files,
        StubWorkstationAudioService? audio = null, IProjectFileService? saves = null) =>
        new(source, new MarkerPlaybackCoordinator(audio ?? new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            files, projectFiles: saves, zoomDelay: (_, token) => Task.Delay(Timeout.Infinite, token));

    private static ProjectData Project(string name = "first") => new() { Offsets = [0], BPMs = [120],
        Tracks = [new Track { Name = name, SelectedSampleFileName = "hit.wav" }] };

    private static async Task Load(WorkstationPageVM vm)
    { await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); }

    [Test]
    public async Task ReloadSamplesPreservesProjectIdentityAndEditingState()
    {
        var project = Project(); var calls = 0; var audio = new StubWorkstationAudioService();
        var instrument = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano/C4.wav" }, Notes = [new() { Key = "C4", Timestamp = 500 }] };
        project.Tracks.Add(instrument);
        var source = new StubWorkstationSourceService { Load = (_, _) => { calls++; return Task.FromResult(new WorkstationSource("map", "song", project)); } };
        var files = new StubSampleFileService(["hit.wav", "piano/C4.wav"]);
        var saves = new DelayedProjectFileService(); saves.Release(); var vm = Create(source, files, audio, saves);
        try
        {
            await Load(vm); var track = vm.Tracks[0]; vm.PlaceSample(track, 125); var node = track.Samples.Single();
            vm.SelectNode(track, node, false); vm.SetTrackName(track, "edited");
            vm.ZoomInCommand.Execute(null); vm.ToggleTracksCollapsedCommand.Execute(null); await vm.FlushSavesAsync();
            await vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(3));
            await vm.PlayPauseCommand.ExecuteAsync(null);
            var position = vm.Position; var generation = vm.LoadGeneration; var waveform = vm.Waveform;
            var writes = saves.Writes; var zoom = vm.ZoomFactor; var selected = vm.SelectedTrack; var nodes = vm.SelectedSamples.ToArray();
            files.List = (_, _) => Task.FromResult<IReadOnlyList<string>>(["hit.wav", "new.wav", "piano/C4.wav"]);
            await vm.ReloadSamplesCommand.ExecuteAsync(null);
            Assert.Multiple(() =>
            {
                Assert.That(vm.Project, Is.SameAs(project)); Assert.That(vm.Tracks[0], Is.SameAs(track));
                Assert.That(track.Samples.Single(), Is.SameAs(node)); Assert.That(track.Name, Is.EqualTo("edited"));
                Assert.That(vm.Waveform, Is.SameAs(waveform)); Assert.That(vm.ZoomFactor, Is.EqualTo(zoom));
                Assert.That(vm.AreTracksCollapsed, Is.True); Assert.That(vm.Position, Is.EqualTo(position));
                Assert.That(vm.IsPlaying, Is.False); Assert.That(vm.LoadGeneration, Is.EqualTo(generation));
                Assert.That(vm.SelectedTrack, Is.SameAs(selected)); Assert.That(vm.SelectedSamples, Is.EqualTo(nodes));
                Assert.That(vm.CanUndo, Is.True); Assert.That(vm.CanRedo, Is.False);
                Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "hit.wav", "new.wav" }));
                Assert.That(instrument.KeyFiles["C4"], Is.EqualTo("piano/C4.wav"));
                Assert.That(audio.SampleUpdates.Last().Any(e => e.FileName == "piano/C4.wav"), Is.True);
                Assert.That(audio.ForceReloadSampleRequests, Is.EqualTo(1));
                Assert.That(calls, Is.EqualTo(1)); Assert.That(saves.Writes, Is.EqualTo(writes));
                Assert.That(vm.LoadPhase, Is.EqualTo(ProjectLoadPhase.Ready)); Assert.That(vm.CanReloadSamples, Is.True);
            });
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task DelayedListingKeepsChoicesAndGatesConflictingEdits()
    {
        var files = new StubSampleFileService(["hit.wav"]);
        var late = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", Project())) };
        var vm = Create(source, files);
        try
        {
            await Load(vm); var track = vm.Tracks[0]; vm.PlaceSample(track, 125);
            Assert.That(vm.BeginNodeDrag(track, track.Samples[0]), Is.True);
            Assert.That(vm.CanReloadSamples, Is.False); vm.CancelNodeDrag();
            files.List = (_, _) => late.Task;
            var reload = vm.ReloadSamplesCommand.ExecuteAsync(null);
            Assert.That(reload.IsCompleted, Is.False); Assert.That(vm.CanReloadSamples, Is.False);
            Assert.That(vm.CanEditTracks, Is.False); Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "hit.wav" }));
            late.SetResult(["new.wav"]); await reload;
            Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "new.wav" }));
            Assert.That(track.SelectedSampleFileName, Is.EqualTo("hit.wav"));
            Assert.That(vm.GetSampleAvailability(track), Is.EqualTo(TrackSampleAvailability.Missing));
            Assert.That(vm.CanUndo, Is.True); Assert.That(vm.CanReloadSamples, Is.True);
        }
        finally { late.TrySetResult([]); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task FailedListingKeepsChoicesAndAllowsRetry()
    {
        var files = new StubSampleFileService(["hit.wav"]); var vm = Create(new(), files);
        try
        {
            await Load(vm); var project = vm.Project;
            files.List = (_, _) => Task.FromException<IReadOnlyList<string>>(new IOException("listing failed"));
            await vm.ReloadSamplesCommand.ExecuteAsync(null);
            Assert.That(vm.Project, Is.SameAs(project)); Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "hit.wav" }));
            Assert.That(vm.ErrorMessage, Is.EqualTo("listing failed")); Assert.That(vm.CanReloadSamples, Is.True);
            files.List = (_, _) => Task.FromResult<IReadOnlyList<string>>(["recovered.wav"]);
            await vm.ReloadSamplesCommand.ExecuteAsync(null);
            Assert.That(vm.ErrorMessage, Is.Null); Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "recovered.wav" }));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LateReloadDoesNotAffectReplacementProjectOrDeactivation(bool deactivate)
    {
        var files = new StubSampleFileService(["hit.wav"]); var vm = Create(new(), files);
        var late = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Load(vm); files.List = (_, _) => late.Task;
        var reload = vm.ReloadSamplesCommand.ExecuteAsync(null);
        try
        {
            Task ending;
            if (deactivate) ending = vm.DeactivateAsync();
            else
            {
                files.List = (_, _) => Task.FromResult<IReadOnlyList<string>>(["newer.wav"]);
                ending = vm.LoadAsync("newer.json", default);
            }
            late.SetResult(["obsolete.wav"]); await reload; await ending;
            Assert.That(vm.SampleFiles, Does.Not.Contain("obsolete.wav"));
            if (!deactivate) Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "newer.wav" }));
        }
        finally { late.TrySetResult([]); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task ReloadDoesNotWaitForPendingSaveButDeactivationDrainsIt()
    {
        var project = Project(); var saves = new DelayedProjectFileService();
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) };
        var vm = Create(source, new(["hit.wav"]), saves: saves);
        try
        {
            await Load(vm); vm.SetTrackName(vm.Tracks[0], "pending name");
            await vm.ReloadSamplesCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(3));
            var ending = vm.DeactivateAsync(); Assert.That(ending.IsCompleted, Is.False);
            saves.Release(); await ending;
            Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks[0].Name, Is.EqualTo("pending name"));
        }
        finally { saves.Release(); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task ReloadWaitsForPlaybackUpdatesAndCannotRunTwice()
    {
        var files = new StubSampleFileService(["hit.wav"]); var audio = new StubWorkstationAudioService();
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", Project())) };
        var vm = Create(source, files, audio);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Load(vm); audio.PrepareSamplesAsync = _ => pending.Task;
            vm.PlaceSample(vm.Tracks[0], 125);
            var listings = 0;
            files.List = (_, _) => { listings++; return Task.FromResult<IReadOnlyList<string>>(["hit.wav"]); };
            var reload = vm.ReloadSamplesCommand.ExecuteAsync(null);
            Assert.That(vm.ReloadSamplesCommand.CanExecute(null), Is.False);
            Assert.That(listings, Is.Zero);
            await vm.ReloadSamplesCommand.ExecuteAsync(null);
            Assert.That(listings, Is.Zero);
            audio.PrepareSamplesAsync = null; pending.SetResult(); await reload;
            Assert.That(listings, Is.EqualTo(1)); Assert.That(audio.ForceReloadSampleRequests, Is.EqualTo(1));
        }
        finally { pending.TrySetResult(); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task ReturnCancelsAndObservesPendingReload()
    {
        var files = new StubSampleFileService(["hit.wav"]); var vm = Create(new(), files);
        var pending = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Load(vm); files.List = (_, _) => pending.Task;
            var reload = vm.ReloadSamplesCommand.ExecuteAsync(null);
            var returning = vm.PrepareToReturnAsync(default);
            Assert.That(returning.IsCompleted, Is.False);
            pending.SetResult(["obsolete.wav"]); await reload;
            Assert.That(await returning, Is.True);
            Assert.That(vm.SampleFiles, Does.Not.Contain("obsolete.wav"));
        }
        finally { pending.TrySetResult([]); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task SaveFailureDoesNotPreventSampleReloadOrLoseEdits()
    {
        var project = Project(); var saves = new FailOnceProjectFileService();
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) };
        var vm = Create(source, new(["hit.wav"]), saves: saves);
        try
        {
            await Load(vm); vm.SetTrackName(vm.Tracks[0], "keep this edit");
            Assert.ThrowsAsync<IOException>(async () => await vm.FlushSavesAsync());
            var saveError = vm.SaveError;
            await vm.ReloadSamplesCommand.ExecuteAsync(null);
            Assert.That(vm.Project, Is.SameAs(project)); Assert.That(vm.Tracks[0].Name, Is.EqualTo("keep this edit"));
            Assert.That(vm.SaveError, Is.EqualTo(saveError)); Assert.That(vm.CanReloadSamples, Is.True);
            vm.RetrySaveCommand.Execute(null); await vm.FlushSavesAsync();
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task FailedInitialLoadCannotUseSampleOnlyReload()
    {
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromException<WorkstationSource>(new IOException("bad project")) };
        var vm = Create(source, new([]));
        try { await vm.LoadAsync("bad.json", default); Assert.That(vm.CanReloadSamples, Is.False); }
        finally { await vm.DeactivateAsync(); }
    }
}
