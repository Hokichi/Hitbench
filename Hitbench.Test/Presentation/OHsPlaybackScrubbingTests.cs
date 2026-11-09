using System.Windows;
using Hitbench.Core.Entities;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsPlaybackScrubbingTests
{
    [Test]
    public void ScrubSeeksScrollsAtStationaryEdgeAndStopsOnReset()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var vm = WorkstationSkinDefaultsTests.Create(new ProjectData { Offsets = [0], BPMs = [120] }, new());
        UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        try
        {
            var released = 0;
            var timeline = new OHsWorkstationTimeline { DataContext = vm, CaptureScrubPointer = () => true, ReleaseScrubPointer = () => released++ };
            UiTestPump.Layout(timeline, 600, 200);
            Assert.That(timeline.BeginScrub(100), Is.True);
            UiTestPump.Run(timeline.NavigationCompletion);
            var start = vm.Position;
            timeline.UpdateScrub(700); timeline.TickScrub(TimeSpan.FromMilliseconds(40));
            UiTestPump.Layout(timeline, 600, 200);
            timeline.TickScrub(TimeSpan.FromMilliseconds(40));
            UiTestPump.Run(timeline.NavigationCompletion);
            Assert.That(vm.Position, Is.GreaterThan(start));
            Assert.That(timeline.IsScrubAutoScrollActive, Is.True);
            timeline.Reset();
            Assert.That(timeline.IsScrubbing, Is.False); Assert.That(timeline.IsScrubAutoScrollActive, Is.False);
            Assert.That(released, Is.EqualTo(1)); Assert.That(vm.IsPlaying, Is.False);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }
}
