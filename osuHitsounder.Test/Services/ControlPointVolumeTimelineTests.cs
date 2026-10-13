using osuHitsounder.Core.Entities;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class ControlPointVolumeTimelineTests
{
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
