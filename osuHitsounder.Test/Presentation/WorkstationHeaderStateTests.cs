using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public sealed class WorkstationHeaderStateTests
{
    [Test]
    public async Task HeaderFollowsProjectAndPlaybackTiming()
    {
        var project = new ProjectData { Offsets = [100, 1000, 1000], BPMs = [120, 150, 180],
            ControlPoints = [new() { Timestamp = 0 }, new() { Timestamp = 0, IsDisabled = true }] };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync), new ImmediateUiScheduler());
        try
        {
            await vm.LoadAsync(Path.Combine(Path.GetTempPath(), "Project name", "project.json"), default);
            Assert.That(vm.ProjectName, Is.EqualTo("Project name"));
            Assert.That(vm.ControlPointCount, Is.EqualTo(2));
            vm.Position = TimeSpan.Zero;
            Assert.That(vm.CurrentBpmText, Is.EqualTo("120 BPM"));
            vm.Position = TimeSpan.FromMilliseconds(1000);
            Assert.That(vm.CurrentBpmText, Is.EqualTo("180 BPM"));
        }
        finally { await vm.DeactivateAsync(); }
    }
}
