using Hitbench.Test;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public class RuntimeTransportFailureTests
{
    [Test]
    public async Task DeactivateReleasesRemainingOwnersAfterStopFailure()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]) })) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        audio.Stopping = () => throw new IOException("stop failed");
        await vm.DeactivateAsync();
        Assert.That(audio.Disposed, Is.True);
        Assert.That(vm.CanPlay, Is.False);
    }

    [Test]
    public async Task InitialSnapshotFailureIsContainedByEveryTransportCommand()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]) })) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler());
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            audio.ReadingSnapshot = () => throw new IOException("poll failed");
            await vm.PlayPauseCommand.ExecuteAsync(null);
            Assert.That(vm.ErrorMessage, Does.Contain("poll failed"));
            await vm.NextWholeBeatCommand.ExecuteAsync(null);
            Assert.That(vm.ErrorMessage, Does.Contain("poll failed"));
        }
        finally { audio.ReadingSnapshot = null; await vm.DeactivateAsync(); }
    }
}
