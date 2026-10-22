using System.Windows;
using Soundie.Resources.DataModels;
using Soundie.Views.Pages;

namespace Soundie.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsControlPointLaneTests
{
    [Test]
    public void NonfinitePointerCannotStartZoneGesture()
    {
        var point = new ControlPointSnapshot(Guid.NewGuid(), 0, 50, "Normal");
        var lane = Create([point]); lane.CapturePointer = () => true; lane.ReleasePointer = () => { };
        lane.BeginZoneGesture(point.Id, new Point(double.NaN, 100), 200);
        lane.UpdateZoneGesture(new Point(100, 50));
        Assert.That(lane.IsVolumePreviewActive, Is.False);
        Assert.That(lane.IsDragPreviewActive, Is.False);
    }

    [Test]
    public void ClippedAndNarrowZonesResolveOnlyActualBoundaries()
    {
        ControlPointSnapshot[] points = [new(Guid.NewGuid(), 0, 50, "Normal"), new(Guid.NewGuid(), 1000, 50, "Soft"), new(Guid.NewGuid(), 1004, 50, "Drum")];
        var lane = Create(points); lane.ContentWidth = 10000; lane.HorizontalOffset = 500;
        Assert.That(lane.HitTestTimingBoundary(points[0].Id, 0), Is.Null);
        lane.HorizontalOffset = 900;
        Assert.That(lane.HitTestTimingBoundary(points[1].Id, 101), Is.EqualTo(points[1].Id));
        Assert.That(lane.HitTestTimingBoundary(points[1].Id, 103), Is.EqualTo(points[2].Id));
        Assert.That(lane.HitTestTimingBoundary(points[1].Id, 102), Is.EqualTo(points[1].Id));
        Assert.That(lane.GetZoneCursor(points[1].Id, 101), Is.SameAs(System.Windows.Input.Cursors.SizeWE));
        Assert.That(lane.GetZoneCursor(points[2].Id, 200), Is.SameAs(System.Windows.Input.Cursors.Hand));
        lane.IsEnabled = false;
        Assert.That(lane.GetZoneCursor(points[1].Id, 101), Is.SameAs(System.Windows.Input.Cursors.Arrow));
    }

    [Test]
    public void OffscreenNextPointStillOwnsRightBoundary()
    {
        ControlPointSnapshot[] points = [new(Guid.NewGuid(), 0, 50, "Normal"), new(Guid.NewGuid(), 1000, 50, "Soft")];
        var lane = Create(points); lane.ContentWidth = 10000; lane.ViewportWidth = 1000;
        UiTestPump.Layout(lane, 1000, 200);
        Assert.That(lane.RealizedPointCount, Is.EqualTo(1));
        Assert.That(lane.HitTestTimingBoundary(points[0].Id, 999), Is.EqualTo(points[1].Id));
    }

    [Test]
    public void FinalEdgeAndZeroWidthLegacyTargetsHaveNoResizeHandle()
    {
        ControlPointSnapshot[] points = [new(Guid.NewGuid(), 0, 50, "Normal"), new(Guid.NewGuid(), 1000, 50, "Soft"), new(Guid.NewGuid(), 1000, 50, "Drum")];
        var lane = Create(points); lane.ContentWidth = lane.ViewportWidth = 10000;
        Assert.That(lane.HitTestTimingBoundary(points[1].Id, 1000), Is.Null);
        Assert.That(lane.HitTestTimingBoundary(points[2].Id, 10000), Is.Null);
        Assert.That(lane.HitTestTimingBoundary(points[2].Id, 1000), Is.EqualTo(points[2].Id));
    }

    [Test]
    public void PlaybackUpdatesDoNotResetZonePreview()
    {
        var point = new ControlPointSnapshot(Guid.NewGuid(), 0, 50, "Normal");
        var lane = Create([point]);
        lane.CapturePointer = () => true;
        lane.ReleasePointer = () => { };
        lane.BeginVolumeDrag(point.Id, 100, 200);
        lane.UpdateVolumeDrag(50);
        lane.Position = TimeSpan.FromMilliseconds(100);
        Assert.That(lane.PlaybackMarkerX, Is.EqualTo(200));
        Assert.That(lane.IsVolumePreviewActive, Is.True);
        Assert.That(lane.PreviewVolume, Is.EqualTo(75));
        lane.CancelVolumeDrag();
        lane.HorizontalOffset = 100;
        Assert.That(lane.PlaybackMarkerX, Is.EqualTo(100));
        lane.Position = TimeSpan.FromMilliseconds(1000);
        Assert.That(lane.PlaybackMarkerX, Is.Null);
    }

    [Test]
    public void LongZoneStartingBeforeViewportIsRealizedAndFocusDoesNotJump()
    {
        ControlPointSnapshot[] points = [new(Guid.NewGuid(), 0, 50, "Normal"), new(Guid.NewGuid(), 9000, 50, "Soft")];
        var lane = Create(points); lane.ContentWidth = 10000; lane.HorizontalOffset = 4000;
        UiTestPump.Layout(lane, 400, 180); var requests = 0;
        lane.HorizontalOffsetRequested += _ => requests++;
        Assert.That(lane.BringPointIntoView(points[0].Id)!.Point!.Id, Is.EqualTo(points[0].Id));
        Assert.That(requests, Is.Zero);
    }

    internal static OHsControlPointLane Create(IReadOnlyList<ControlPointSnapshot> points)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var lane = new OHsControlPointLane();
        lane.DurationMilliseconds = 10000; lane.ContentWidth = 20000; lane.ViewportWidth = 400;
        lane.State = new(new("project.json", Guid.NewGuid()), points, 1, true, null, false, null, 0, true);
        UiTestPump.Layout(lane, 400, 180);
        return lane;
    }

    [Test]
    public void FullBoundsIncludeOffscreenPointsWhileContainersRecycle()
    {
        var points = Enumerable.Range(0, 1000).Select(i => new ControlPointSnapshot(Guid.NewGuid(), i * 10, 100, "Normal")).ToArray();
        var lane = Create(points);
        Assert.That(lane.GetExtents(), Has.Count.EqualTo(1000));
        Assert.That(lane.RealizedPointCount, Is.LessThan(points.Length / 2));
        var original = lane.GetExtents()[800];
        lane.HorizontalOffset = 16000; UiTestPump.Layout(lane, 400, 180);
        Assert.That(lane.RecycledPointCount, Is.GreaterThan(0));
        Assert.That(lane.GetExtents()[800], Is.EqualTo(original));
        Assert.That(lane.RealizedPointCount, Is.LessThan(points.Length / 2));
    }

    [Test]
    public void CoincidentIdentitiesRemainDiscoverableThroughSharedScrollAndTagContract()
    {
        var first = new ControlPointSnapshot(Guid.NewGuid(), 8000, 4, "Drum"); var second = first with { Id = Guid.NewGuid() };
        var lane = Create([first, second]); double? requested = null;
        lane.HorizontalOffsetRequested += offset => { requested = offset; lane.HorizontalOffset = offset; };
        var tag = lane.BringPointIntoView(second.Id);
        Assert.That(requested, Is.Not.Null); Assert.That(tag!.Point!.Id, Is.EqualTo(second.Id));
        Assert.That(lane.GetExtents(), Has.Count.EqualTo(2));
        UiControlContract.AssertStyle<OHsControlPointZone>(tag, "OHsControlPointZoneStyle");
        UiControlContract.AssertTemplate(tag, "OHsControlPointZoneTemplate");
        Assert.That(tag.Label, Does.Contain("Drum").And.Contain("4%"));
        Assert.That(System.Windows.Automation.AutomationProperties.GetName(tag), Does.Contain("8000"));
    }

    [Test]
    public void LogicalTabTraversalReachesOffscreenAndOverlappingIdentities()
    {
        var points = new[] { new ControlPointSnapshot(Guid.NewGuid(), 0, 50, "Normal"), new(Guid.NewGuid(), 8000, 4, "Drum"), new(Guid.NewGuid(), 8000, 75, "Soft") };
        var lane = Create(points);
        lane.HorizontalOffsetRequested += offset => lane.HorizontalOffset = offset;
        Assert.That(lane.MovePointFocus(points[0].Id, true)!.Point!.Id, Is.EqualTo(points[1].Id));
        Assert.That(lane.MovePointFocus(points[1].Id, true)!.Point!.Id, Is.EqualTo(points[2].Id));
        Assert.That(lane.MovePointFocus(points[2].Id, true), Is.Null);
        Assert.That(lane.MovePointFocus(points[2].Id, false)!.Point!.Id, Is.EqualTo(points[1].Id));
    }
}
