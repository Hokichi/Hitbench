using Hitbench.Test;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Hitbench.Core.Entities;

namespace Hitbench.Test.Presentation;

public class WorkstationMarkerModeStateTests
{
    [Test]
    public async Task StaticNodeSelectionPreservesDynamicFollowPreference()
    {
        var node = new Sample { FileName = "a.wav", Timestamp = 100 };
        var track = new Track { SelectedSampleFileName = "a.wav", Samples = [node] };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "audio",
            new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]), Tracks = [track] })) };
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            vm.SetMarkerMode(true); Assert.That(vm.SelectNode(track, node, false), Is.True);
            vm.SetMarkerMode(false); Assert.That(vm.ScrollWithMarker, Is.True);
            vm.SelectNode(track, node, false); Assert.That(vm.ScrollWithMarker, Is.False);
        }
        finally { await vm.DeactivateAsync(); }
    }
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
