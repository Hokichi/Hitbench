using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class WorkstationReturnTests
{
    private static async Task<WorkstationPageVM> ReadyAsync(IProjectFileService saves, StubWorkstationAudioService audio,
        Track? track = null, StubTrackRemovalDialogService? removal = null)
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], Tracks = [track ?? new Track()] };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project))
        }, new MarkerPlaybackCoordinator(audio, MarkerPlaybackCoordinator.DelayAsync), new ImmediateUiScheduler(),
            new StubSampleFileService(["hit.wav"]), projectFiles: saves, trackRemovalDialog: removal);
        await vm.LoadAsync("project.json", default);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        return vm;
    }

    [Test]
    public async Task ReturnShowsStatusWhilePauseWaitsForInFlightSnapshot()
    {
        var saves = new DelayedProjectFileService();
        saves.Release();
        var audio = new StubWorkstationAudioService();
        var track = new Track { Samples = [new Sample { FileName = "hit.wav", Timestamp = 500 }] };
        var vm = await ReadyAsync(saves, audio, track);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        audio.ReadingSnapshot = () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException();
        };
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromSeconds(1) };
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var returning = vm.PrepareToReturnAsync(default);
        try
        {
            Assert.That(returning.IsCompleted, Is.False);
            Assert.That(vm.IsSavingForReturn, Is.True);
        }
        finally { release.Set(); await returning; await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task ReturnWaitsForLatestSaveAndBlocksEditsWithoutDisposingProject()
    {
        var saves = new DelayedProjectFileService();
        var audio = new StubWorkstationAudioService();
        var vm = await ReadyAsync(saves, audio);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        vm.SetTrackVolume(vm.Tracks.Single(), 75);
        var returning = vm.PrepareToReturnAsync(default);
        await StubWorkstationAudioService.WaitUntilAsync(() => vm.IsSavingForReturn && !audio.Snapshot.IsPlaying);
        try
        {
            Assert.That(returning.IsCompleted, Is.False);
            Assert.That(vm.IsReturning, Is.True);
            Assert.That(vm.CanEditTracks, Is.False);
            Assert.That(vm.PlayPauseCommand.CanExecute(null), Is.False);
            Assert.That(audio.Snapshot.IsPlaying, Is.False);
            Assert.That(audio.Disposed, Is.False);
            Assert.That(await vm.PrepareToReturnAsync(default), Is.False);
            vm.SetTrackVolume(vm.Tracks.Single(), 10);
        }
        finally { saves.Release(); }
        Assert.That(await returning, Is.True);
        Assert.That(vm.IsSavingForReturn, Is.False);
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks.Single().Volume, Is.EqualTo(75));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task FailedReturnKeepsProjectUsableAndRetrySaves()
    {
        var saves = new FailOnceProjectFileService();
        var audio = new StubWorkstationAudioService();
        var vm = await ReadyAsync(saves, audio);
        Assert.That(await vm.PrepareToReturnAsync(default), Is.False);
        Assert.That(vm.SaveError, Does.Contain("Disk full"));
        Assert.That(vm.IsReturning, Is.False);
        Assert.That(vm.IsSavingForReturn, Is.False);
        Assert.That(vm.CanEditTracks, Is.True);
        Assert.That(audio.Disposed, Is.False);
        vm.SetTrackVolume(vm.Tracks.Single(), 35);
        Assert.That(await vm.PrepareToReturnAsync(default), Is.True);
        Assert.That(ProjectDataSerializer.Deserialize(saves.SavedJson!).Tracks.Single().Volume, Is.EqualTo(35));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task ReturnIncludesPendingRemovalBeforeSnapshot()
    {
        var response = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var saves = new DelayedProjectFileService();
        saves.Release();
        var vm = await ReadyAsync(saves, new StubWorkstationAudioService(),
            track: new Track { Samples = [new Sample { FileName = "hit.wav" }] }, removal:
            new StubTrackRemovalDialogService { Confirm = _ => response.Task });
        var removing = vm.RemoveTrackCommand.ExecuteAsync(vm.Tracks.Single());
        var returning = vm.PrepareToReturnAsync(default);
        await StubWorkstationAudioService.WaitUntilAsync(() => vm.IsSavingForReturn);
        Assert.That(returning.IsCompleted, Is.False);
        response.SetResult(true);
        await removing;
        Assert.That(await returning, Is.True);
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks, Is.Empty);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task CancellationWaitsForDurableSaveThenRestoresInteraction()
    {
        var saves = new DelayedProjectFileService();
        var vm = await ReadyAsync(saves, new StubWorkstationAudioService());
        using var cancellation = new CancellationTokenSource();
        var returning = vm.PrepareToReturnAsync(cancellation.Token);
        await StubWorkstationAudioService.WaitUntilAsync(() => vm.IsSavingForReturn);
        cancellation.Cancel();
        Assert.That(returning.IsCompleted, Is.False);
        saves.Release();
        Assert.That(await returning, Is.False);
        Assert.That(vm.IsSavingForReturn, Is.False);
        Assert.That(vm.CanEditTracks, Is.True);
        Assert.That(saves.LatestJson, Is.Not.Null);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task ReturnWaitsForPendingImportAndPlaybackPreparation()
    {
        var imported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var files = new StubSampleFileService(["hit.wav"]) { Import = (_, _, _) => imported.Task };
        var saves = new DelayedProjectFileService();
        saves.Release();
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(audio, MarkerPlaybackCoordinator.DelayAsync), new ImmediateUiScheduler(),
            files, new StubSampleFileDialogService("hit.wav"), saves);
        await vm.LoadAsync("project.json", default);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        var importing = vm.ImportSamplesAsync();
        var returning = vm.PrepareToReturnAsync(default);
        await StubWorkstationAudioService.WaitUntilAsync(() => vm.IsSavingForReturn);
        Assert.That(returning.IsCompleted, Is.False);
        Assert.That(vm.CanImportSample, Is.False);
        imported.SetResult("hit.wav");
        await importing;
        Assert.That(await returning, Is.True);
        Assert.That(saves.LatestJson, Is.Not.Null);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task FailedLoadReturnsWithoutSerializingPartialProject()
    {
        var saves = new DelayedProjectFileService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromException<WorkstationSource>(new IOException("Load failed"))
        }, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync),
            new ImmediateUiScheduler(), projectFiles: saves);
        await vm.LoadAsync("project.json", default);
        Assert.That(await vm.PrepareToReturnAsync(default), Is.True);
        Assert.That(saves.Writes, Is.Zero);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task ReturnCancelsLoadingWithoutWritingPartialProject()
    {
        var source = new StubWorkstationSourceService
        {
            Load = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return null!; }
        };
        var saves = new DelayedProjectFileService();
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync),
            new ImmediateUiScheduler(), projectFiles: saves);
        var loading = vm.LoadAsync("project.json", default);
        Assert.That(await vm.PrepareToReturnAsync(default), Is.True);
        await loading.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(saves.Writes, Is.Zero);
        await vm.DeactivateAsync();
    }
}
