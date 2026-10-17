using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Helpers;

namespace osuHitsounder.Test.Presentation;

public class OHsControlPointLayoutTests
{
    [Test]
    public void MappingUsesSharedContentCoordinates() => Assert.That(OHsControlPointLayout.GetLeft(1000, 4000, 800) - 80, Is.EqualTo(120));

    [TestCase(150, 40, false)][TestCase(160, 40, true)][TestCase(60, 40, true)]
    public void OverlapUsesWholePillAndAllowsTouchingEdges(double left, double width, bool expected)
    {
        OHsControlPointExtent[] extents = [new(Guid.NewGuid(), 500, 100, 60)];
        Assert.That(OHsControlPointLayout.CanPlace(Guid.Empty, 750, left, width, extents), Is.EqualTo(expected));
    }

    [Test]
    public void OnlyMovingIdentityIsExcludedAndDuplicateTimestampAlwaysRejects()
    {
        var moving = Guid.NewGuid(); var other = Guid.NewGuid();
        OHsControlPointExtent[] extents = [new(moving, 500, 100, 60), new(other, 500, 100, 0)];
        Assert.That(OHsControlPointLayout.CanPlace(moving, 500, 300, 40, extents), Is.False);
        Assert.That(OHsControlPointLayout.CanPlace(moving, 1000, 100, 40, extents.Take(1).ToArray()), Is.True);
    }

    [TestCase(double.NaN, 40)][TestCase(100, double.PositiveInfinity)][TestCase(100, -1)]
    public void UnknownOrInvalidBoundsCannotAuthorizePlacement(double left, double width) =>
        Assert.That(OHsControlPointLayout.CanPlace(Guid.Empty, 1000, left, width, []), Is.False);

    [Test]
    public void OffscreenAndUnknownOtherExtentsStillParticipate()
    {
        Assert.That(OHsControlPointLayout.CanPlace(Guid.Empty, 2000, 1200, 50, [new(Guid.NewGuid(), 1900, 1190, 40)]), Is.False);
        Assert.That(OHsControlPointLayout.CanPlace(Guid.Empty, 2000, 1200, 50, [new(Guid.NewGuid(), 1900, double.NaN, 40)]), Is.False);
        Assert.That(OHsControlPointLayout.GetLeft(1, 0, 800), Is.NaN);
    }
}
