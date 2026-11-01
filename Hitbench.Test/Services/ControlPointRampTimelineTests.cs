using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.Services.ProjectFiles;

namespace Hitbench.Test.Services;

public class ControlPointRampTimelineTests
{
    [Test]
    public void CollapsedFrameIntervalSelectsLaterEndpointWithoutDivision()
    {
        var segment = new ControlPointVolumeSegment(0, 1, 0, 0, 0, 100, true);
        Assert.That(segment.EvaluateGain(0), Is.EqualTo(1));
        var curve = new ControlPointVolumeTimeline([new(int.MinValue, 0, IsRamp: true), new(int.MaxValue, 100)]);
        Assert.That(curve.GetGain(0), Is.EqualTo(.5f).Within(1e-6));
    }
    [Test]
    public void RisingRampUsesPointOwnedEndpoints()
    {
        var curve = new ControlPointVolumeTimeline([new(0, 10), new(1000, 10, IsRamp: true), new(3000, 50)]);
        Assert.That(new[] { 500, 1000, 1500, 2000, 2500, 3000 }.Select(t => curve.GetGain(ControlPointVolumeTimeline.ToFrame(t))),
            Is.EqualTo(new[] { .1f, .1f, .2f, .3f, .4f, .5f }).Within(1e-6));
    }

    [Test]
    public void NegativeAnchorAndDisabledInteriorRetainSlope()
    {
        var curve = new ControlPointVolumeTimeline([new(-1000, 10, IsRamp: true), new(0, 99, true), new(1000, 50, IsRamp: true)]);
        Assert.That(curve.GetGain(0), Is.EqualTo(.3f).Within(1e-6));
        Assert.That(curve.GetVolumeAtTimestamp(0), Is.EqualTo(30));
        Assert.That(curve.GetGain(long.MaxValue), Is.EqualTo(.5f));
    }

    [Test]
    public void ConsecutiveFallingEqualAndZeroEndpoints()
    {
        var curve = new ControlPointVolumeTimeline([new(1000, 0, IsRamp: true), new(2000, 100, IsRamp: true), new(3000, 50, IsRamp: true), new(4000, 50)]);
        Assert.That(curve.GetGain(0), Is.EqualTo(1));
        Assert.That(curve.GetGain(66150), Is.EqualTo(.5f));
        Assert.That(curve.GetGain(110250), Is.EqualTo(.75f));
        Assert.That(curve.GetGain(154350), Is.EqualTo(.5f));
    }

    [Test]
    public void MissingRampIsFixedAndRoundTripPreservesValues()
    {
        var legacy = ProjectDataSerializer.Deserialize("{\"ControlPoints\":[{\"Timestamp\":0,\"Volume\":4}]}");
        Assert.That(legacy.ControlPoints[0].IsRamp, Is.False);
        legacy.ControlPoints[0].IsRamp = true;
        var point = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(legacy)).ControlPoints[0];
        Assert.That((point.Volume, point.IsRamp), Is.EqualTo((4, true)));
    }

    [TestCase("null"), TestCase("1"), TestCase("\"true\"")]
    public void MalformedRampIsRejected(string value) => Assert.Throws<InvalidDataException>(() =>
        ProjectDataSerializer.Deserialize("{\"ControlPoints\":[{\"Timestamp\":0,\"IsRamp\":" + value + "}]}"));

    [Test]
    public void DuplicateRampAliasesAreRejected() => Assert.Throws<InvalidDataException>(() =>
        ProjectDataSerializer.Deserialize("{\"ControlPoints\":[{\"Timestamp\":0,\"IsRamp\":true,\"isramp\":false}]}"));
}
