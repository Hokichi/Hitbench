using System.Windows;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsControlPointLaneTests
{
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
        UiControlContract.AssertStyle<OHsControlPointTag>(tag, "OHsControlPointTagStyle");
        UiControlContract.AssertTemplate(tag, "OHsControlPointTagTemplate");
        Assert.That(tag.Label, Is.EqualTo("Drum - 4%"));
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
