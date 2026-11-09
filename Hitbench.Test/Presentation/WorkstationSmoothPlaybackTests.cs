using System.Windows;
using Hitbench.Core.Entities;
using Hitbench.Resources.CustomControls;
using Hitbench.ViewModels.Shell;
using Hitbench.Services;
using Hitbench.Views.Pages;
using System.Windows.Controls;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class WorkstationSmoothPlaybackTests
{
    [TestCase(32)] [TestCase(128)]
    public void ExplicitFramesMoveSharedOffsetWithoutScrollingLayoutOrSeeking(int zoom)
    {
        var audio = new StubWorkstationAudioService();
        var playback = new MarkerPlaybackCoordinator(audio, (_, token) => Task.Delay(Timeout.Infinite, token));
        var project = new ProjectData { Offsets = [0], BPMs = [120], IsMarkerStatic = true, WorkstationZoom = zoom,
            Tracks = [new Track { Samples = [new Sample { Timestamp = 2000 }] }], ControlPoints = [new() { Timestamp = 2000 }] };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "audio", project)) };
        var vm = new WorkstationPageVM(source, playback, new Hitbench.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 700 });
        try
        {
            UiTestPump.Run(page.LoadCompletion); view.ProcessFrames = false; page.AutomaticPresentationFrames = false;
            UiTestPump.Run(vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(2)));
            UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
            page.ProcessPresentationFrame(TimeSpan.FromSeconds(1000));
            audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(2010) };
            UiTestPump.Run(playback.UpdateSamplesAsync("project.json", [], default));
            page.ProcessPresentationFrame(TimeSpan.FromSeconds(1000.01));
            page.UpdateLayout();
            var waveform = (WaveformView)page.Timeline.FindName("WaveformDisplay");
            var viewer = (ScrollViewer)page.Timeline.FindName("WaveformScrollViewer");
            var grid = WorkstationControlTree.Descendants(page).OfType<BeatGridView>().Single();
            var lane = (OHsControlPointLane)page.Timeline.FindName("ControlPointLane");
            waveform.PrepareDrawing(); grid.PrepareDrawing();
            var physical = viewer.HorizontalOffset; var builds = waveform.ContentBuildCount; var rows = grid.ContentBuildCount;
            var measures = lane.PanelMeasureCount; var oldOffset = page.TimelineHorizontalOffset; var seeks = audio.SeekCalls;
            for (var i = 1; i <= 50; i++)
            {
                page.ProcessPresentationFrame(TimeSpan.FromSeconds(1000.01) + TimeSpan.FromTicks(i * 2500));
                page.UpdateLayout(); waveform.PrepareDrawing(); grid.PrepareDrawing();
                Assert.That(page.TimelineHorizontalOffset, Is.GreaterThan(oldOffset)); oldOffset = page.TimelineHorizontalOffset;
                Assert.That(waveform.HorizontalOffset, Is.EqualTo(oldOffset)); Assert.That(grid.HorizontalOffset, Is.EqualTo(oldOffset));
                Assert.That(lane.HorizontalOffset, Is.EqualTo(oldOffset));
                Assert.That(((ScrollBar)page.FindName("WaveformScrollBar")).Value, Is.EqualTo(oldOffset));
            }
            Assert.That(viewer.HorizontalOffset, Is.EqualTo(physical)); Assert.That(waveform.ContentBuildCount, Is.EqualTo(builds));
            Assert.That(grid.ContentBuildCount, Is.EqualTo(rows)); Assert.That(lane.PanelMeasureCount, Is.EqualTo(measures)); Assert.That(audio.SeekCalls, Is.EqualTo(seeks));
            Assert.That(vm.DisplayPosition, Is.EqualTo(TimeSpan.FromMilliseconds(2022.5)));
            UiTestPump.Run(vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(1)));
            page.ProcessPresentationFrame(TimeSpan.FromSeconds(1000.03));
            Assert.That(vm.DisplayPosition, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(page.TimelineHorizontalOffset, Is.EqualTo(page.TimelineContentWidth / 10 - page.TimelineViewportWidth / 2).Within(.001));
            var requested = page.TimelineHorizontalOffset + page.TimelineViewportWidth / 8;
            var target = Hitbench.Resources.Helpers.OHsTimelineViewport.GetPositionAt(page.TimelineViewportWidth / 2, requested, page.TimelineContentWidth, TimeSpan.FromSeconds(10))!.Value;
            seeks = audio.SeekCalls;
            page.Timeline.ScrollFromUser(requested); UiTestPump.Run(page.Timeline.NavigationCompletion);
            page.ProcessPresentationFrame(TimeSpan.FromSeconds(1000.04));
            Assert.That(audio.SeekCalls, Is.EqualTo(seeks + 1));
            Assert.That(vm.DisplayPosition, Is.EqualTo(target));
            Assert.That(page.TimelineHorizontalOffset, Is.EqualTo(requested).Within(.001));
            audio.Snapshot = new(TimeSpan.FromSeconds(10), false, null);
            UiTestPump.Run(playback.UpdateSamplesAsync("project.json", [], default)); vm.RefreshPlayback();
            Assert.That(vm.IsPlaying, Is.False);
            Assert.That(page.TimelineHorizontalOffset, Is.EqualTo(page.Timeline.TimelineContentWidth - page.Timeline.TimelineViewportWidth / 2).Within(.001));
            vm.SetMarkerMode(false); page.UpdateLayout(); page.UpdateLayout();
            Assert.That(page.Timeline.TimelineEdgePadding, Is.Zero);
            Assert.That(waveform.IsPlaybackMarkerStatic, Is.False); Assert.That(grid.IsPlaybackMarkerStatic, Is.False);
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }
    [Test]
    public void ControlPointLaneReusesGeometryAndLayoutForFractionalMovement()
    {
        var points = Enumerable.Range(0, 1000).Select(i => new Hitbench.Resources.DataModels.ControlPointSnapshot(Guid.NewGuid(), i * 10, 50, "Normal")).ToArray();
        var lane = OHsControlPointLaneTests.Create(points); lane.IsPlaybackMarkerStatic = true; lane.HorizontalOffset = 400;
        UiTestPump.Layout(lane, 400, 180);
        var geometry = lane.GeometryBuildCount; var measures = lane.PanelMeasureCount;
        for (var i = 1; i <= 100; i++) { lane.HorizontalOffset = 400 + i / 4d; lane.UpdateLayout(); }
        Assert.That(lane.GeometryBuildCount, Is.EqualTo(geometry)); Assert.That(lane.PanelMeasureCount, Is.EqualTo(measures));
        Assert.That(lane.RealizedPointCount, Is.LessThan(100));
        Assert.That(lane.HitTestTimingBoundary(points[25].Id, 500 - 425), Is.EqualTo(points[25].Id));
        lane.HorizontalOffset = 601; lane.UpdateLayout();
        Assert.That(lane.GeometryBuildCount, Is.EqualTo(geometry)); Assert.That(lane.PanelMeasureCount, Is.GreaterThan(measures));
        Assert.That(lane.RealizedPointCount, Is.LessThan(100));
    }

    [Test]
    public void StaticFractionalMovementPreservesVolumeGesture()
    {
        var point = new Hitbench.Resources.DataModels.ControlPointSnapshot(Guid.NewGuid(), 0, 50, "Normal");
        var lane = OHsControlPointLaneTests.Create([point]); lane.IsPlaybackMarkerStatic = true;
        lane.CapturePointer = () => true; lane.ReleasePointer = () => { };
        lane.BeginVolumeDrag(point.Id, 100, 200); lane.UpdateVolumeDrag(50);
        lane.HorizontalOffset = .25;
        Assert.That(lane.IsVolumePreviewActive, Is.True); Assert.That(lane.PreviewVolume, Is.EqualTo(75));
        lane.ResetInteraction();
    }

    [Test]
    public void PinnedZeroDurationTargetStaysReachableDuringRetainedMovement()
    {
        var first = new Hitbench.Resources.DataModels.ControlPointSnapshot(Guid.NewGuid(), 8000, 50, "Normal");
        var second = first with { Id = Guid.NewGuid() };
        var lane = OHsControlPointLaneTests.Create([first, second]); lane.IsPlaybackMarkerStatic = true;
        lane.HorizontalOffsetRequested += offset => lane.HorizontalOffset = offset;
        var target = lane.BringPointIntoView(first.Id)!;
        lane.HorizontalOffset = 17000; lane.UpdateLayout();
        Assert.That(target.TranslatePoint(new Point(), lane).X, Is.EqualTo(0).Within(.001));
        lane.HorizontalOffset = 17000.25; lane.UpdateLayout();
        Assert.That(target.TranslatePoint(new Point(), lane).X, Is.EqualTo(0).Within(.001));
    }
    [Test]
    public void SampleGridRetainsContentWhileHitTestingUsesLiveOffset()
    {
        var node = new Sample { Timestamp = 3000 };
        var grid = new BeatGridView { Duration = TimeSpan.FromSeconds(10), ContentWidth = 1600, ViewportWidth = 200,
            IsPlaybackMarkerStatic = true, HorizontalOffset = 400, Samples = [node], CanPlaceSamples = true };
        UiTestPump.Layout(grid, 200, 60); grid.PrepareDrawing(); var builds = grid.ContentBuildCount;
        for (var i = 1; i <= 100; i++) { grid.HorizontalOffset = 400 + i / 4d; grid.PrepareDrawing(); }
        Assert.That(grid.ContentBuildCount, Is.EqualTo(builds));
        Assert.That(grid.FindSampleAt(new Point(480 - 425, 30)), Is.SameAs(node));
        grid.HorizontalOffset = 500.25; grid.PrepareDrawing(); Assert.That(grid.ContentBuildCount, Is.EqualTo(builds + 1));
    }
    [Test]
    public void PianoRollRetainsTimeDrawingAndUsesLiveNoteBounds()
    {
        var note = new Hitbench.Resources.DataModels.InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 3000);
        var roll = new PianoRollView { Duration = TimeSpan.FromSeconds(10), ContentWidth = 1600, ViewportWidth = 200,
            KeyColumnWidth = 60, LaneHeight = 20, VerticalOffset = 1200, IsPlaybackMarkerStatic = true, HorizontalOffset = 400, Notes = [note] };
        UiTestPump.Layout(roll, 260, 400); roll.PrepareDrawing(); var builds = roll.ContentBuildCount;
        var before = roll.GetNoteBounds(note);
        for (var i = 1; i <= 100; i++) { roll.HorizontalOffset = 400 + i / 4d; roll.PrepareDrawing(); }
        Assert.That(roll.ContentBuildCount, Is.EqualTo(builds));
        var after = roll.GetNoteBounds(note); Assert.That(after.Left, Is.EqualTo(before.Left - 25));
        Assert.That(roll.FindNoteAt(new Point(after.Left + after.Width / 2, after.Top + after.Height / 2)), Is.EqualTo(note));
    }
    [SetUp]
    public void Setup() => new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();

    [Test]
    public void WaveformRetainsContentAcrossFractionalOffsets()
    {
        var waveform = new WaveformView { Data = new WaveformData(TimeSpan.FromSeconds(10), Enumerable.Repeat(.5f, 1000).ToArray()),
            ViewportWidth = 200, ZoomFactor = 8, IsPlaybackMarkerStatic = true, HorizontalOffset = 400 };
        UiTestPump.Layout(waveform, 1600, 100); waveform.PrepareDrawing();
        var drawing = waveform.ContentDrawing;
        var builds = waveform.ContentBuildCount;
        for (var i = 1; i <= 400; i++) { waveform.HorizontalOffset = 400 + i / 4d; waveform.PrepareDrawing(); }
        Assert.That(waveform.ContentDrawing, Is.SameAs(drawing)); Assert.That(waveform.ContentBuildCount, Is.EqualTo(builds));
        waveform.HorizontalOffset = 500.25; waveform.PrepareDrawing();
        Assert.That(waveform.ContentBuildCount, Is.EqualTo(builds + 1));
    }
}
