using Soundie.Core.Entities;
using Soundie.Services;

namespace Soundie.Test.Services;

public class ControlPointVolumeTimelineTests
{
    [Test]
    public void DisabledChainsAndLastDuplicateInheritPreviousEffectiveVolume()
    {
        var timeline = new ControlPointVolumeTimeline([new(0, 20, true), new(1000, 80), new(2000, 10),
            new(2000, 20, true), new(3000, 60, true), new(4000, 40)]);
        Assert.That(new[] { 0L, 44100L, 88200L, 132300L, 176400L }.Select(timeline.GetGain),
            Is.EqualTo(new[] { 1f, .8f, .8f, .8f, .4f }));
    }

    [Test]
    public void LatestPointWinsAndInputIsCopied()
    {
        var points = new List<ControlPointVolumeSnapshot> { new(1000, 50), new(900, 75), new(1000, 25), new(2000, 100), new(3000, 0) };
        var timeline = new ControlPointVolumeTimeline(points); points.Clear();
        Assert.That(timeline.GetGain(0), Is.EqualTo(1));
        Assert.That(timeline.GetGain(39690), Is.EqualTo(.75f));
        Assert.That(timeline.GetGain(44100), Is.EqualTo(.25f));
        Assert.That(timeline.GetGain(88200), Is.EqualTo(1));
        Assert.That(timeline.GetGain(132300), Is.Zero);
        Assert.That(timeline.GetNextBoundary(44100), Is.EqualTo(88200));
        Assert.That(timeline.GetNextBoundary(132300), Is.EqualTo(long.MaxValue));
    }
    [Test]
    public void EmptyAndMillisecondRounding()
    {
        Assert.That(ControlPointVolumeTimeline.Empty.GetGain(long.MaxValue), Is.EqualTo(1));
        Assert.That(ControlPointVolumeTimeline.Empty.GetNextBoundary(0), Is.EqualTo(long.MaxValue));
        var timeline = new ControlPointVolumeTimeline([new(1, 50)]);
        Assert.That(timeline.GetGain(43), Is.EqualTo(1)); Assert.That(timeline.GetGain(44), Is.EqualTo(.5f));
    }
    [TestCase(-1, 50)][TestCase(0, -1)][TestCase(0, 101)]
    public void InvalidPointsFail(int time, int volume) => Assert.Throws<ArgumentException>(() => new ControlPointVolumeTimeline([new(time, volume)]));
}
