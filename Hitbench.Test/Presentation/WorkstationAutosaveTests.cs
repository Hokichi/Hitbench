using Hitbench.Test;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
namespace Hitbench.Test.Presentation;

public sealed class WorkstationAutosaveTests
{
    [Test]
    public async Task QuietPeriodRestartsAtLatestEdit()
    {
        var clock = new ManualTimeProvider();
        var delay = new ManualDelay(clock);
        var project = new ProjectData { Tracks = [new Track()], UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new()] };
        var files = new DelayedProjectFileService(); files.Release();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            projectFiles: files, timeProvider: clock, saveDelay: delay.WaitAsync);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            vm.ToggleTracksCollapsedCommand.Execute(null);
            delay.Advance(TimeSpan.FromMilliseconds(50));
            vm.ToggleTracksCollapsedCommand.Execute(null);
            await StubWorkstationAudioService.WaitUntilAsync(() => delay.RequestCount == 2);
            delay.Advance(TimeSpan.FromMilliseconds(149));
            Assert.That(vm.SaveSnapshotCount, Is.Zero);
            delay.Advance(TimeSpan.FromMilliseconds(1));
            await StubWorkstationAudioService.WaitUntilAsync(() => files.Writes == 1);
            await vm.FlushSavesAsync();
            Assert.That(vm.SaveSnapshotCount, Is.EqualTo(1));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task ContinuousEditsPublishByMaximumDirtyInterval()
    {
        var clock = new ManualTimeProvider(); clock.SetUtcNow(DateTimeOffset.UtcNow);
        var delay = new ManualDelay(clock);
        var project = new ProjectData { Tracks = [new Track()], UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new()] };
        var files = new DelayedProjectFileService(); files.Release();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            projectFiles: files, timeProvider: clock, saveDelay: delay.WaitAsync);
        try
        {
            await vm.LoadAsync("project.json",default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            vm.ToggleTracksCollapsedCommand.Execute(null);
            for (var i = 1; i < 10; i++)
            {
                delay.Advance(TimeSpan.FromMilliseconds(100));
                vm.ToggleTracksCollapsedCommand.Execute(null);
                await StubWorkstationAudioService.WaitUntilAsync(() => delay.RequestCount == i + 1);
                Assert.That(vm.SaveSnapshotCount, Is.Zero);
            }
            delay.Advance(TimeSpan.FromMilliseconds(100));
            await StubWorkstationAudioService.WaitUntilAsync(() => vm.SaveSnapshotCount == 1);
            await vm.FlushSavesAsync();
            Assert.That(files.Writes, Is.EqualTo(1));
            Assert.That(vm.HasPendingSaves, Is.False);
        }
        finally { await vm.DeactivateAsync(); }
    }
}
