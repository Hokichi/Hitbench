using Hitbench.Resources.DataModels;
using Hitbench.Resources.Helpers;

namespace Hitbench.Test.Presentation;

public class OHsControlPointRampProjectionTests
{
    [Test]
    public void ProjectionClipsWithoutChangingSlope()
    {
        ControlPointSnapshot[] points = [new(Guid.NewGuid(), 1000, 10, "Normal", IsRamp: true), new(Guid.NewGuid(), 3000, 50, "Soft")];
        var piece = OHsControlPointLayout.GetCurveProjections(points, 5000, 1500, 2500).Single();
        Assert.That((piece.StartVolume, piece.EndVolume), Is.EqualTo((20d, 40d)));
        Assert.That(piece.ConnectorFromVolume, Is.Null);
    }
    [Test]
    public void FixedJumpIsOwnedByRightZoneAndDisabledPointsAreNotEndpoints()
    {
        ControlPointSnapshot[] points = [new(Guid.NewGuid(), 0, 10, "Normal"), new(Guid.NewGuid(), 1000, 99, "Normal", true), new(Guid.NewGuid(), 3000, 50, "Soft")];
        var pieces = OHsControlPointLayout.GetCurveProjections(points, 5000);
        Assert.That(pieces[1].StartVolume, Is.EqualTo(10));
        Assert.That(pieces[1].ConnectorFromVolume, Is.Null);
        Assert.That(pieces[2].ConnectorFromVolume, Is.EqualTo(10));
        Assert.That(pieces.Count(p => p.ConnectorFromVolume is not null), Is.EqualTo(1));
    }
}
