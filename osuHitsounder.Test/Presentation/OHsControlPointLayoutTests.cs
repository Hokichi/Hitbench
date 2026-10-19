using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Helpers;

namespace osuHitsounder.Test.Presentation;

public class OHsControlPointLayoutTests
{
    [TestCase(1000, 4000, 800, 80, 400, 120)]
    [TestCase(0, 4000, 800, 0, 400, 0)]
    [TestCase(4000, 4000, 800, 400, 400, 400)]
    [TestCase(1000, 4000, 1600, 200, 400, 200)]
    [TestCase(1000, 0, 800, 80, 400, null)]
    [TestCase(1000, 4000, 0, 80, 400, null)]
    [TestCase(1000, 4000, 800, 80, 0, null)]
    [TestCase(1000, 4000, 800, -1, 400, null)]
    [TestCase(-1, 4000, 800, 0, 400, null)]
    [TestCase(4001, 4000, 800, 0, 400, null)]
    [TestCase(1000, 4000, 800, 300, 400, null)]
    [TestCase(3000, 4000, 800, 0, 400, null)]
    [TestCase(double.NaN, 4000, 800, 0, 400, null)]
    [TestCase(1000, double.PositiveInfinity, 800, 0, 400, null)]
    [TestCase(1000, 4000, double.NaN, 0, 400, null)]
    [TestCase(1000, 4000, 800, double.PositiveInfinity, 400, null)]
    [TestCase(1000, 4000, 800, 0, double.NaN, null)]
    public void PlaybackMarkerUsesExplicitTimelineCoordinates(double position, double duration, double width, double offset, double viewport, double? expected) =>
        Assert.That(OHsControlPointLayout.GetPlaybackMarkerX(position, duration, width, offset, viewport), Is.EqualTo(expected));

    [Test]
    public void MappingUsesSharedContentCoordinates() => Assert.That(OHsControlPointLayout.GetLeft(1000, 4000, 800) - 80, Is.EqualTo(120));

    [Test]
    public void IntervalsShareBoundariesAndFinalZoneEndsAtDuration()
    {
        ControlPointSnapshot[] points = [new(Guid.NewGuid(), 1000, 50, "Normal"), new(Guid.NewGuid(), 3000, 25, "Soft")];
        var extents = OHsControlPointLayout.GetExtents(points, 10000, 1000);
        Assert.That(extents.Select(e => (e.Left, e.Width)), Is.EqualTo(new[] { (100d, 200d), (300d, 700d) }));
        Assert.That(OHsControlPointLayout.CanPlace(Guid.Empty, 1001, 10000, points), Is.True);
        Assert.That(OHsControlPointLayout.CanPlace(Guid.Empty, 1000, 10000, points), Is.False);
        Assert.That(OHsControlPointLayout.CanPlace(points[0].Id, 1000, 10000, points), Is.True);
    }

    [Test]
    public void EarlierDuplicatesAreZeroLengthButRemainInStableOrder()
    {
        ControlPointSnapshot[] points = [new(Guid.NewGuid(), 1000, 20, "Normal"), new(Guid.NewGuid(), 1000, 50, "Soft"), new(Guid.NewGuid(), 3000, 75, "Drum")];
        var extents = OHsControlPointLayout.GetExtents(points, 10000, 1000);
        Assert.That(extents.Select(e => e.Id), Is.EqualTo(points.Select(p => p.Id)));
        Assert.That(extents.Select(e => e.Width), Is.EqualTo(new[] { 0d, 200d, 700d }));
        Assert.That(OHsControlPointLayout.CanPlace(points[0].Id, 1000, 10000, points), Is.False);
    }

    [Test]
    public void OutOfRangePointsAreClippedAndSubpixelIntervalsRemainValid()
    {
        ControlPointSnapshot[] points = [new(Guid.NewGuid(), 1, 0, "Normal"), new(Guid.NewGuid(), 2, 4, "Soft"), new(Guid.NewGuid(), 10000, 50, "Normal"), new(Guid.NewGuid(), 12000, 50, "Normal")];
        var extents = OHsControlPointLayout.GetExtents(points, 10000, 1000);
        Assert.That(extents[0].Width, Is.EqualTo(.1).Within(.00001));
        Assert.That(extents.Skip(2).Select(e => (e.Left, e.Width)), Is.EqualTo(new[] { (1000d, 0d), (1000d, 0d) }));
        Assert.That(OHsControlPointLayout.CanPlace(Guid.Empty, 10000, 10000, points), Is.False);
        Assert.That(OHsControlPointLayout.GetExtents([], 10000, 1000), Is.Empty);
    }

    [TestCase(0, 1000)][TestCase(double.NaN, 1000)][TestCase(1000, double.PositiveInfinity)]
    public void InvalidGeometryProducesNoExtents(double duration, double width) => Assert.That(OHsControlPointLayout.GetExtents([new(Guid.NewGuid(), 0, 50, "Normal")], duration, width), Is.Empty);
}
