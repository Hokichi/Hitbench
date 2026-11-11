using Hitbench.Test;
using Hitbench.Core.Entities;

namespace Hitbench.Test.Core;

public sealed class ProjectDataControlPointTests
{
    [TestCase(250)]
    [TestCase(-100)]
    public void OffsetPointIsCreatedOnceAndPreservesEdits(int offset)
    {
        var project = new ProjectData { UninheritedPoints = TimingTestData.Points([offset, 1000], []) };
        Assert.That(project.EnsureOffsetControlPoint(), Is.True);
        var point = project.ControlPoints.Single();
        Assert.That((point.Timestamp, point.Volume, point.Sampleset, point.IsDisabled), Is.EqualTo((offset, 100, "Normal", false)));
        Assert.That(project.IsOffsetControlPoint(point), Is.True);
        point.Volume = 50; point.Sampleset = "Drum"; point.IsDisabled = true;
        Assert.That(project.EnsureOffsetControlPoint(), Is.False);
        Assert.That(project.ControlPoints.Single(), Is.SameAs(point));
        Assert.That((point.Volume, point.Sampleset, point.IsDisabled), Is.EqualTo((50, "Drum", true)));
        Assert.That(project.IsOffsetControlPoint(new ControlPoint { Timestamp = offset + 1 }), Is.False);
    }

    [Test]
    public void MissingTimingDoesNotInventAnOffset()
    {
        var project = new ProjectData();
        Assert.That(project.EnsureOffsetControlPoint(), Is.False);
        Assert.That(project.ControlPoints, Is.Empty);
        Assert.That(project.IsOffsetControlPoint(new()), Is.False);
    }
}
