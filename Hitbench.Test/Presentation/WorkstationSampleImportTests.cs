using Hitbench.Test;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public sealed class WorkstationSampleImportTests
{
    private static async Task<WorkstationPageVM> Ready(StubSampleFileService files, StubSampleFileDialogService dialog,
        StubWorkstationAudioService? audio = null)
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(audio ?? new(), MarkerPlaybackCoordinator.DelayAsync),
            new ImmediateUiScheduler(), files, dialog, messenger: new WeakReferenceMessenger());
        await vm.LoadAsync("project.json", default);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        return vm;
    }

    [Test]
    public async Task ImportPassesCapturedProjectBeatmapFolder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BeatmapFolder");
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new Hitbench.Core.Entities.WorkstationSource("map.osu", "audio.wav",
                new Hitbench.Core.Entities.ProjectData { BeatmapPath = directory, UninheritedPoints = TimingTestData.Points([0], [120]) }))
        };
        var dialog = new StubSampleFileDialogService(Array.Empty<string>());
        var model = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync),
            new ImmediateUiScheduler(), new StubSampleFileService([]), dialog, messenger: new WeakReferenceMessenger());
        try
        {
            await model.LoadAsync("project.json", default);
            model.AcknowledgePresentationReady(model.LoadGeneration);
            await model.ImportSamplesAsync();
            Assert.That(dialog.InitialDirectory, Is.EqualTo(directory));
            Assert.That(dialog.Context?.ProjectPath, Is.EqualTo("project.json"));
            Assert.That(dialog.Context?.SongDirectory, Is.EqualTo(directory));
        }
        finally { await model.DeactivateAsync(); }
    }

    [Test]
    public async Task BatchImportsCheckedPathsAndRefreshesOnce()
    {
        List<string> attempted = [];
        var files = new StubSampleFileService(["old.wav"])
        { Import = (_, source, _) => { attempted.Add(source); return Task.FromResult(Path.GetFileName(source)); } };
        var vm = await Ready(files, new StubSampleFileDialogService(new[] { "a.wav", "b.ogg", "c.mp3", "A.WAV" }));
        vm.AddSampleTrackCommand.Execute(null);
        var track = vm.Tracks.Single();
        track.SelectedSampleFileName = null;
        var refreshes = 0;
        files.List = (_, _) => { refreshes++; return Task.FromResult<IReadOnlyList<string>>(["old.wav", "a.wav", "b.ogg", "c.mp3"]); };
        Assert.That(await vm.ImportSamplesAsync(), Is.EqualTo(new[] { "a.wav", "b.ogg", "c.mp3" }));
        Assert.That(attempted, Is.EqualTo(new[] { "a.wav", "b.ogg", "c.mp3" }));
        Assert.That(refreshes, Is.EqualTo(1));
        Assert.That(track.SelectedSampleFileName, Is.Null);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task FailureInMiddleStillImportsRemainingFiles()
    {
        List<string> attempted = [];
        var files = new StubSampleFileService([]) { Import = (_, source, _) =>
        {
            attempted.Add(source);
            return source == "b.ogg" ? Task.FromException<string>(new IOException("Access denied")) : Task.FromResult(source);
        } };
        var vm = await Ready(files, new StubSampleFileDialogService(new[] { "a.wav", "b.ogg", "c.mp3" }));
        files.List = (_, _) => Task.FromResult<IReadOnlyList<string>>(["a.wav", "c.mp3"]);
        Assert.That(await vm.ImportSamplesAsync(), Is.EqualTo(new[] { "a.wav", "c.mp3" }));
        Assert.That(attempted, Is.EqualTo(new[] { "a.wav", "b.ogg", "c.mp3" }));
        Assert.That(vm.ErrorMessage, Does.Contain("b.ogg"));
        Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "a.wav", "c.mp3" }));
        Assert.That(vm.CanImportSample, Is.True);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task DialogCancellationCopiesNothing()
    {
        var files = new StubSampleFileService([]) { Import = (_, _, _) => throw new AssertionException("Unexpected copy") };
        var vm = await Ready(files, new StubSampleFileDialogService(Array.Empty<string>()));
        Assert.That(await vm.ImportSamplesAsync(), Is.Empty);
        Assert.That(vm.CanImportSample, Is.True);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task PausePrecedesSelectionAndDoesNotAutoResume()
    {
        var audio = new StubWorkstationAudioService();
        var dialog = new StubSampleFileDialogService(Array.Empty<string>()) { Selection = _ =>
        {
            Assert.That(audio.Snapshot.IsPlaying, Is.False);
            return Task.FromResult<IReadOnlyList<string>>([]);
        } };
        var vm = await Ready(new([]), dialog, audio);
        audio.Play();
        await vm.ImportSamplesAsync();
        Assert.That(audio.Snapshot.IsPlaying, Is.False);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task ReturnAndFlushAwaitEntireBatch()
    {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var files = new StubSampleFileService([]) { Import = (_, source, _) => ++count == 2 ? pending.Task : Task.FromResult(source) };
        var vm = await Ready(files, new StubSampleFileDialogService(new[] { "a.wav", "b.ogg" }));
        var importing = vm.ImportSamplesAsync();
        await StubWorkstationAudioService.WaitUntilAsync(() => count == 2);
        var flush = vm.FlushSavesAsync();
        var returning = vm.PrepareToReturnAsync(default);
        Assert.That(flush.IsCompleted, Is.False);
        Assert.That(returning.IsCompleted, Is.False);
        pending.SetResult("b.ogg");
        await importing;
        await flush;
        Assert.That(await returning, Is.True);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task CancelledOrObsoleteBatchCannotUpdateAnotherProject()
    {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var files = new StubSampleFileService(["old.wav"]) { Import = (_, _, _) => { count++; return pending.Task; } };
        var vm = await Ready(files, new StubSampleFileDialogService(new[] { "a.wav", "b.ogg" }));
        var importing = vm.ImportSamplesAsync();
        await StubWorkstationAudioService.WaitUntilAsync(() => count == 1);
        var loading = vm.LoadAsync("other.json", default);
        pending.SetResult("a.wav");
        Assert.That(await importing, Is.Empty);
        await loading;
        Assert.That(count, Is.EqualTo(1));
        Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "old.wav" }));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task RefreshFailureReportsErrorAndSettlesBusyState()
    {
        var files = new StubSampleFileService([]);
        var vm = await Ready(files, new StubSampleFileDialogService("a.wav"));
        files.List = (_, _) => Task.FromException<IReadOnlyList<string>>(new IOException("Cannot refresh"));
        Assert.That(await vm.ImportSamplesAsync(), Is.EqualTo(new[] { "a.wav" }));
        Assert.That(vm.ErrorMessage, Does.Contain("Cannot refresh"));
        Assert.That(vm.CanImportSample, Is.True);
        await vm.DeactivateAsync();
    }
}
