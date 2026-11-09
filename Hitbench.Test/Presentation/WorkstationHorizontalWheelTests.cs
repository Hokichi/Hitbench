using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class WorkstationHorizontalWheelTests
{
    [SetUp]
    public void Setup() => new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();

    [Test]
    public void PlainWheelIsScopedAndPartialDeltasAccumulate()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        using var view = new InMemoryView(new Window { Content = page, Width = 1000, Height = 700 });
        try
        {
            UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            view.UpdateLayout(); view.UpdateLayout();
            Assert.That(page.HandleContextWheel(ModifierKeys.None, -60, page.AudioCard), Is.True);
            view.UpdateLayout(); Assert.That(page.TimelineHorizontalOffset, Is.Zero);
            Assert.That(page.HandleContextWheel(ModifierKeys.None, -60, page.AudioCard), Is.True);
            view.UpdateLayout(); Assert.That(page.TimelineHorizontalOffset, Is.GreaterThan(0));
            Assert.That(page.HandleContextWheel(ModifierKeys.None, -120, page.Toolbar), Is.False);
            var bar = (ScrollBar)page.FindName("WaveformScrollBar");
            var before = page.TimelineHorizontalOffset;
            Assert.That(page.HandleContextWheel(ModifierKeys.None, -120, bar), Is.True);
            view.UpdateLayout(); Assert.That(page.TimelineHorizontalOffset, Is.GreaterThan(before));
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    [TestCase(false)] [TestCase(true)]
    public void StaticUserScrollingSeeksAndDraggingMovesTracksInPointerDirection(bool playing)
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler());
        var timeline = new OHsWorkstationTimeline { DataContext = vm, CaptureScrubPointer = () => true, ReleaseScrubPointer = () => { } };
        try
        {
            UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            UiTestPump.Run(vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(5)));
            if (playing) UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
            vm.SetMarkerMode(true); UiTestPump.Layout(timeline, 600, 180); timeline.ApplyMarkerMode(); UiTestPump.Layout(timeline, 600, 180);
            var before = vm.Position;
            timeline.ScrollFromUser(timeline.TimelineHorizontalOffset - 100);
            UiTestPump.Run(timeline.NavigationCompletion); UiTestPump.Layout(timeline, 600, 180);
            Assert.That(vm.Position, Is.LessThan(before)); Assert.That(vm.IsPlaying, Is.EqualTo(playing));
            if (!playing)
            {
                before = vm.Position;
                timeline.HandleWaveformWheel(ModifierKeys.None, -120);
                timeline.HandleWaveformWheel(ModifierKeys.None, -120);
                UiTestPump.Run(timeline.NavigationCompletion);
                Assert.That(vm.Position.TotalSeconds, Is.EqualTo(before.TotalSeconds + 32 / timeline.TimelineContentWidth * 10).Within(.00001));
                UiTestPump.Layout(timeline, 600, 180);
            }
            before = vm.Position;
            Assert.That(timeline.BeginScrub(100), Is.True);
            Assert.That(vm.Position, Is.EqualTo(before), "pointer down must not jump");
            timeline.UpdateScrub(180); UiTestPump.Run(timeline.NavigationCompletion);
            Assert.That(vm.Position, Is.LessThan(before));
            timeline.EndScrub(); Assert.That(timeline.IsScrubbing, Is.False);
        }
        finally { timeline.Reset(); UiTestPump.Run(timeline.CancelNavigationAsync()); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [Test]
    public void StaticClickSeeksButCancelledClickDoesNot()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler());
        var timeline = new OHsWorkstationTimeline { DataContext = vm, CaptureScrubPointer = () => true, ReleaseScrubPointer = () => { } };
        try
        {
            UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            vm.SetMarkerMode(true); UiTestPump.Layout(timeline, 600, 180); timeline.ApplyMarkerMode(); UiTestPump.Layout(timeline, 600, 180);
            timeline.BeginScrub(400); timeline.CompleteScrub(400); UiTestPump.Run(timeline.NavigationCompletion);
            Assert.That(vm.Position, Is.GreaterThan(TimeSpan.Zero));
            var calls = audio.SeekCalls;
            timeline.BeginScrub(200); timeline.Reset();
            Assert.That(audio.SeekCalls, Is.EqualTo(calls));
            Assert.That(timeline.IsScrubbing, Is.False);
        }
        finally { UiTestPump.Run(timeline.CancelNavigationAsync()); UiTestPump.Run(vm.DeactivateAsync()); }
    }
}
