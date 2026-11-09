using Hitbench.Services;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public class WorkstationMarkerModeStateTests
{
    [Test]
    public async Task ModePreservesTransportHistoryAndFollowPreference()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler());
        try
        {
            await vm.LoadAsync("project.json", default);
            vm.AcknowledgePresentationReady(vm.LoadGeneration);
            await vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(3));
            Assert.That(vm.IsStaticMarker, Is.False);
            Assert.That(vm.CanToggleScrollWithMarker, Is.True);
            vm.SetMarkerMode(true);
            Assert.Multiple(() =>
            {
                Assert.That(vm.IsStaticMarker, Is.True);
                Assert.That(vm.CanToggleScrollWithMarker, Is.False);
                Assert.That(vm.ScrollWithMarker, Is.True);
                Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromSeconds(3)));
                Assert.That(vm.IsPlaying, Is.False);
                Assert.That(vm.CanUndo, Is.False);
                Assert.That(vm.HasPendingSaves, Is.False);
            });
            vm.SetMarkerMode(false);
            Assert.That(vm.CanToggleScrollWithMarker, Is.True);
            vm.IsReturning = true;
            Assert.That(vm.CanToggleScrollWithMarker, Is.False);
        }
        finally { await vm.DeactivateAsync(); audio.Dispose(); }
    }
}
