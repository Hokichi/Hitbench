using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public sealed class WorkstationTrackExtentCacheTests
{
    [Test]
    public async Task ExtentsReuseAggregateAndRemovingTallestRecomputes()
    {
        var first = new Track { IsCollapsed = true }; var second = new Track { IsCollapsed = true };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map", "song", new ProjectData { Tracks = [first, second] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", default);
        try
        {
            vm.SetTrackMinimumExtent(first, true, 50); vm.SetTrackMinimumExtent(second, true, 80);
            Assert.That(vm.GetTrackExtent(first), Is.EqualTo(80));
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++) _ = vm.GetTrackExtent(first);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
            vm.Tracks.Remove(second);
            Assert.That(vm.GetTrackExtent(first), Is.EqualTo(50));
        }
        finally { await vm.DeactivateAsync(); }
    }
}
