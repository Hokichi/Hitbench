using Hitbench.Test;
using System.Windows;
using Hitbench.Core.Entities;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Helpers;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class WorkstationStaticMarkerTests
{
    [SetUp]
    public void Setup() => new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();

    [TestCase(0)] [TestCase(5)] [TestCase(10)]
    public void SharedViewportCentersAtBothAudioEdges(double seconds)
    {
        var audio = new StubWorkstationAudioService();
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "audio",
            new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]), Tracks = [new Track { SelectedSampleFileName = "a.wav", Samples = [new Sample { Timestamp = 0, FileName = "a.wav" }] }] })) };
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        var page = new WorkstationPage(vm);
        var window = new Window { Content = page, Width = 1100, Height = 700 };
        using var view = new InMemoryView(window);
        try
        {
            UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            view.UpdateLayout(); view.UpdateLayout();
            var width = page.Timeline.TimelineContentWidth;
            UiTestPump.Run(vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(seconds)));
            vm.SetMarkerMode(true); page.Timeline.ApplyMarkerMode();
            view.UpdateLayout(); view.UpdateLayout(); page.Timeline.FollowPlaybackPosition(); view.UpdateLayout();
            Assert.That(page.Timeline.TimelineContentWidth, Is.EqualTo(width).Within(.01));
            Assert.That(page.Timeline.TimelineHorizontalOffset,
                Is.EqualTo(seconds / 10 * width - page.Timeline.TimelineViewportWidth / 2).Within(.01));
            Assert.That(page.Timeline.MinimumHorizontalOffset, Is.EqualTo(-page.Timeline.TimelineViewportWidth / 2));
            Assert.That(audio.SeekCalls, Is.EqualTo(1), "following must not seek");
            var grid = WorkstationControlTree.Descendants(page).OfType<BeatGridView>().Single();
            Assert.That(grid.IsPlaybackMarkerStatic, Is.True);
            var timeViewport = page.Timeline.GetTimeViewportBounds(page);
            var trackMarker = grid.TranslatePoint(new Point(grid.GetPlaybackMarkerX()!.Value, 0), page).X;
            Assert.That(trackMarker, Is.EqualTo(timeViewport.Left + timeViewport.Width / 2).Within(.01));
            window.Width = 900; view.UpdateLayout(); view.UpdateLayout(); page.Timeline.FollowPlaybackPosition(); view.UpdateLayout();
            Assert.That(page.Timeline.TimelineHorizontalOffset,
                Is.EqualTo(seconds / 10 * page.Timeline.TimelineContentWidth - page.Timeline.TimelineViewportWidth / 2).Within(.01));
            vm.ZoomInCommand.Execute(null);
            view.UpdateLayout(); view.UpdateLayout(); page.Timeline.FollowPlaybackPosition(); view.UpdateLayout();
            Assert.That(page.Timeline.TimelineHorizontalOffset,
                Is.EqualTo(seconds / 10 * page.Timeline.TimelineContentWidth - page.Timeline.TimelineViewportWidth / 2).Within(.01));
            vm.SetMarkerMode(false); page.Timeline.ApplyMarkerMode(); view.UpdateLayout(); view.UpdateLayout();
            Assert.That(page.Timeline.MinimumHorizontalOffset, Is.Zero);
            Assert.That(page.Timeline.TimelineEdgePadding, Is.Zero);
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    [Test]
    public void ControlPointBoundaryHitTestingSupportsLeadingPadding()
    {
        var vm = WorkstationControlPointStateTests.Create(new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]),
            ControlPoints = [new() { Timestamp = 1000, Volume = 50 }] }, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(vm));
            var state = vm.ControlPointsState!;
            var lane = new OHsControlPointLane { State = state, DurationMilliseconds = 10000, ContentWidth = 1000,
                ViewportWidth = 300, HorizontalOffset = -150, HorizontalScrollPadding = 150 };
            UiTestPump.Layout(lane, 300, 60);
            var point = state.Points.Single(p => p.Timestamp == 1000 && !p.IsOffsetAnchor);
            Assert.That(lane.HitTestTimingBoundary(point.Id, 250), Is.EqualTo(point.Id));
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [Test]
    public void SignedOffsetPreservesNodeHitTestingAndControlPointMarker()
    {
        var sample = new Sample { Timestamp = 0 };
        var grid = new BeatGridView { Duration = TimeSpan.FromSeconds(10), ContentWidth = 1000, ViewportWidth = 200,
            HorizontalOffset = -100, IsPlaybackMarkerStatic = true, CanPlaceSamples = true, Samples = [sample] };
        UiTestPump.Layout(grid, 200, 60);
        Assert.That(grid.GetPlaybackMarkerX(), Is.EqualTo(100));
        var bounds = grid.GetSampleBounds(0);
        Assert.That(grid.FindSampleAt(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2)), Is.SameAs(sample));
        Assert.That(OHsControlPointLayout.GetPlaybackMarkerX(0, 10000, 1000, -100, 200), Is.EqualTo(100));
        var piano = new PianoRollView { Duration = TimeSpan.FromSeconds(10), ContentWidth = 1000, ViewportWidth = 200,
            KeyColumnWidth = 50, HorizontalOffset = -100, HorizontalScrollPadding = 100, IsPlaybackMarkerStatic = true };
        UiTestPump.Layout(piano, 250, 100);
        Assert.That(piano.PlaybackMarkerX, Is.EqualTo(150));
    }

    [Test]
    public void BlankTrailingSpaceContainsNoBeatsBeyondAudioDuration()
    {
        var drawing = new System.Windows.Media.DrawingGroup();
        using (var context = drawing.Open())
            BeatGridDrawing.Draw(context, new System.Windows.Controls.Border(), TimingTestData.Points([0], [120]), 1, 10000, 1000, 900, 200, 60, false);
        var lines = drawing.Children.OfType<System.Windows.Media.GeometryDrawing>()
            .Select(d => d.Geometry).OfType<System.Windows.Media.LineGeometry>().ToArray();
        Assert.That(lines, Is.Not.Empty);
        Assert.That(lines.All(line => line.StartPoint.X <= 100), Is.True, "audio ends at viewport x=100 for these explicit inputs");
    }
}
