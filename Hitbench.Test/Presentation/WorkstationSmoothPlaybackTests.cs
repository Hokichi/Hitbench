using System.Windows;
using Hitbench.Core.Entities;
using Hitbench.Resources.CustomControls;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class WorkstationSmoothPlaybackTests
{
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
