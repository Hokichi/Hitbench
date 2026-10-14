using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class WorkstationProjectRefreshTests
{
    private static WorkstationPageVM Create(StubWorkstationSourceService source, IProjectFileService? saves = null) =>
        new(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            new StubSampleFileService(["hit.wav"]), projectFiles: saves, zoomDelay: (_, token) => Task.Delay(Timeout.Infinite, token));

    private static ProjectData Project(string name) => new() { Tracks = [new Track { Name = name, SelectedSampleFileName = "hit.wav" }], Offsets = [0], BPMs = [120] };

    private static async Task Load(WorkstationPageVM vm)
    {
        await vm.LoadAsync("project.json", default);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
    }

    [Test]
    public async Task RefreshReloadsProjectWithoutSavingUnchangedSnapshot()
    {
        var first = Project("first"); var reloaded = Project("external"); var calls = 0;
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", ++calls == 1 ? first : reloaded)) };
        var saves = new DelayedProjectFileService(); saves.Release();
        var vm = Create(source, saves);
        try
        {
            await Load(vm); await vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(3)); await vm.PlayPauseCommand.ExecuteAsync(null);
            await vm.RefreshProjectCommand.ExecuteAsync(null);
            Assert.That(vm.Project, Is.SameAs(reloaded));
            Assert.That(vm.Tracks.Single().Name, Is.EqualTo("external"));
            Assert.That(vm.Position, Is.EqualTo(TimeSpan.Zero)); Assert.That(vm.IsPlaying, Is.False);
            Assert.That(vm.LoadPhase, Is.EqualTo(ProjectLoadPhase.AwaitingPresentation));
            Assert.That(vm.CanRefreshProject, Is.False); Assert.That(saves.Writes, Is.Zero);
            vm.AcknowledgePresentationReady(vm.LoadGeneration); Assert.That(vm.CanRefreshProject, Is.True);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task RefreshWaitsForNameAndZoomSave()
    {
        var calls = 0; var saves = new DelayedProjectFileService(); var first = Project("first");
        var source = new StubWorkstationSourceService { Load = (_, _) =>
        {
            calls++;
            return Task.FromResult(new WorkstationSource("map", "song", calls == 1 ? first : ProjectDataSerializer.Deserialize(saves.LatestJson!)));
        } };
        var vm = Create(source, saves);
        try
        {
            await Load(vm); vm.SetTrackName(vm.Tracks.Single(), "renamed"); vm.ZoomInCommand.Execute(null);
            var refresh = vm.RefreshProjectCommand.ExecuteAsync(null);
            Assert.That(refresh.IsCompleted, Is.False); Assert.That(calls, Is.EqualTo(1));
            Assert.That(vm.CanRefreshProject, Is.False); Assert.That(vm.CanEditTracks, Is.False);
            saves.Release(); await refresh;
            Assert.That(vm.Tracks.Single().Name, Is.EqualTo("renamed")); Assert.That(vm.ZoomFactor, Is.EqualTo(33));
            Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).WorkstationZoom, Is.EqualTo(33));
            Assert.That(calls, Is.EqualTo(2));
        }
        finally { saves.Release(); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task SaveFailureAbortsRefreshAndPreservesProject()
    {
        var project = Project("first"); var calls = 0;
        var source = new StubWorkstationSourceService { Load = (_, _) => { calls++; return Task.FromResult(new WorkstationSource("map", "song", project)); } };
        var saves = new FailOnceProjectFileService(); var vm = Create(source, saves);
        try
        {
            await Load(vm); vm.SetTrackName(vm.Tracks.Single(), "rename");
            await vm.RefreshProjectCommand.ExecuteAsync(null);
            Assert.That(vm.Project, Is.SameAs(project)); Assert.That(calls, Is.EqualTo(1));
            Assert.That(vm.ErrorMessage, Does.Contain("Could not save project")); Assert.That(vm.CanEditTracks, Is.True);
            vm.RetrySaveCommand.Execute(null); await vm.FlushSavesAsync();
            await vm.RefreshProjectCommand.ExecuteAsync(null); Assert.That(calls, Is.EqualTo(2));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task LoadFailureAllowsRefreshRetry()
    {
        var calls = 0;
        var source = new StubWorkstationSourceService { Load = (_, _) => ++calls == 2
            ? Task.FromException<WorkstationSource>(new IOException("reload failed"))
            : Task.FromResult(new WorkstationSource("map", "song", Project("loaded"))) };
        var vm = Create(source);
        try
        {
            await Load(vm); await vm.RefreshProjectCommand.ExecuteAsync(null);
            Assert.That(vm.LoadPhase, Is.EqualTo(ProjectLoadPhase.Failed)); Assert.That(vm.ErrorMessage, Is.EqualTo("reload failed"));
            Assert.That(vm.CanRefreshProject, Is.True);
            await vm.RefreshProjectCommand.ExecuteAsync(null); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            Assert.That(vm.CanEditTracks, Is.True); Assert.That(vm.ErrorMessage, Is.Null);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task RefreshRejectsActiveDragAndResetsHistoryAfterReload()
    {
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", Project("loaded"))) };
        var vm = Create(source);
        try
        {
            Assert.That(vm.CanRefreshProject, Is.False); await Load(vm);
            var track = vm.Tracks.Single(); vm.PlaceSample(track, 125); var node = track.Samples.Single();
            Assert.That(vm.CanUndo, Is.True); Assert.That(vm.BeginNodeDrag(track, node), Is.True);
            Assert.That(vm.RefreshProjectCommand.CanExecute(null), Is.False);
            vm.CancelNodeDrag(); Assert.That(vm.CanRefreshProject, Is.True);
            await vm.RefreshProjectCommand.ExecuteAsync(null);
            Assert.That(vm.CanUndo, Is.False); Assert.That(vm.SelectedSamples, Is.Empty);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task CancellingRefreshStillObservesPendingSaveCompletion()
    {
        var saves = new DelayedProjectFileService();
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", Project("loaded"))) };
        var vm = Create(source, saves);
        await Load(vm); vm.SetTrackName(vm.Tracks.Single(), "pending name");
        var refresh = vm.RefreshProjectCommand.ExecuteAsync(null);
        var deactivation = vm.DeactivateAsync();
        try
        {
            var completed = await Task.WhenAny(refresh, Task.Delay(50));
            Assert.That(completed, Is.Not.SameAs(refresh), "Refresh must own its save drain even when cancelled.");
        }
        finally { saves.Release(); await refresh; await deactivation; }
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks.Single().Name, Is.EqualTo("pending name"));
    }

    [Test]
    public async Task NewLoadDiscardsLateRefreshResult()
    {
        var late = new TaskCompletionSource<WorkstationSource>(); var calls = 0;
        var source = new StubWorkstationSourceService { Load = (_, _) => ++calls == 2 ? late.Task :
            Task.FromResult(new WorkstationSource("map", "song", Project(calls == 1 ? "first" : "newer"))) };
        var vm = Create(source);
        try
        {
            await Load(vm); var refresh = vm.RefreshProjectCommand.ExecuteAsync(null);
            await vm.LoadAsync("newer.json", default);
            late.SetResult(new WorkstationSource("map", "song", Project("obsolete"))); await refresh;
            Assert.That(vm.Tracks.Single().Name, Is.EqualTo("newer"));
        }
        finally { late.TrySetResult(new WorkstationSource("map", "song", Project("obsolete"))); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task DeactivationDiscardsLateRefreshResult()
    {
        var late = new TaskCompletionSource<WorkstationSource>(); var calls = 0;
        var source = new StubWorkstationSourceService { Load = (_, _) => ++calls == 1
            ? Task.FromResult(new WorkstationSource("map", "song", Project("first"))) : late.Task };
        var vm = Create(source);
        await Load(vm);
        var refresh = vm.RefreshProjectCommand.ExecuteAsync(null);
        Assert.That(calls, Is.EqualTo(2));
        var deactivation = vm.DeactivateAsync();
        late.SetResult(new WorkstationSource("map", "song", Project("late")));
        await refresh; await deactivation;
        Assert.That(vm.CanRefreshProject, Is.False); Assert.That(vm.Project?.Tracks.FirstOrDefault()?.Name, Is.Not.EqualTo("late"));
    }
}
