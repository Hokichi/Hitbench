using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
namespace osuHitsounder.Test.Presentation;
public class WorkstationFolderCommandsTests
{
    [Test]
    public async Task PendingOpenRejectsDuplicatesAndCancelledFailureCannotOverwriteError()
    {
        var pending = new TaskCompletionSource();
        var launcher = new StubDirectoryLauncher { Open = _ => pending.Task };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), directoryLauncher: launcher);
        await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        var operation = vm.OpenProjectFolderCommand.ExecuteAsync(null);
        Assert.That(vm.OpenBeatmapFolderCommand.CanExecute(null), Is.False);
        vm.IsReturning = true;
        pending.SetException(new IOException("stale failure"));
        await operation;
        Assert.That(vm.ErrorMessage, Is.Not.EqualTo("stale failure"));
        vm.IsReturning = false;
        launcher.Open = _ => Task.FromException(new IOException("launch failed"));
        await vm.OpenProjectFolderCommand.ExecuteAsync(null);
        Assert.That(vm.ErrorMessage, Is.EqualTo("launch failed"));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task OpensAssociatedDirectoriesAndGatesReturning()
    {
        var launcher = new StubDirectoryLauncher();
        var project = new ProjectData { BeatmapPath = Path.GetFullPath("beatmap") };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", project)) };
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), directoryLauncher: launcher);
        await vm.LoadAsync(Path.GetFullPath("project/project.json"), default);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        await vm.OpenBeatmapFolderCommand.ExecuteAsync(null);
        await vm.OpenProjectFolderCommand.ExecuteAsync(null);
        Assert.That(launcher.Directories, Is.EqualTo(new[] { project.BeatmapPath, Path.GetFullPath("project") }));
        vm.IsReturning = true;
        Assert.That(vm.OpenProjectFolderCommand.CanExecute(null), Is.False);
        await vm.DeactivateAsync();
    }
}
