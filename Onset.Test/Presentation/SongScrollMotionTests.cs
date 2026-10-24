using Onset.Services.Ui;
namespace Onset.Test.Presentation;

public class SongScrollMotionTests
{
    [TestCase(-120, 3, 360), TestCase(-240, 3, 720), TestCase(-30, 3, 90)]
    [TestCase(-120, 1, 120), TestCase(-120, 0, 0), TestCase(-120, -1, 960)]
    public void WheelDistanceHonorsSystemLinesAndFractionalDelta(int delta, int lines, double expected)
    {
        var motion = new SongScrollMotion();
        Assert.That(motion.Request(delta, lines, 120, 960, 10000, 0, TimeSpan.Zero, false), Is.EqualTo(expected));
        Assert.That(motion.IsActive, Is.False);
    }
    [Test]
    public void SameDirectionBurstAccumulatesWithoutQueueAndReversalUsesDisplayedOffset()
    {
        var motion = new SongScrollMotion();
        motion.Request(-120, 3, 120, 960, 10000, 0, TimeSpan.Zero, true);
        Assert.That(motion.Advance(TimeSpan.FromMilliseconds(80), 10000), Is.EqualTo(315).Within(.001));
        Assert.That(motion.Advance(TimeSpan.FromMilliseconds(40), 10000), Is.EqualTo(315).Within(.001));
        motion.Request(120, 3, 120, 960, 10000, 315, TimeSpan.FromMilliseconds(80), true);
        Assert.That(motion.Advance(TimeSpan.FromMilliseconds(96), 10000), Is.LessThan(315));
        Assert.That(motion.Advance(TimeSpan.FromMilliseconds(240), 10000), Is.Zero);
        Assert.That(motion.IsActive, Is.False);
        motion.Request(-120, 3, 120, 960, 10000, 0, TimeSpan.FromMilliseconds(240), true);
        motion.Request(-120, 3, 120, 960, 10000, 0, TimeSpan.FromMilliseconds(250), true);
        Assert.That(motion.Advance(TimeSpan.FromMilliseconds(410), 10000), Is.EqualTo(720));
        Assert.That(motion.IsActive, Is.False);
    }
    [Test]
    public void DirectAndReducedMotionCancelEasingAndClampExtent()
    {
        var motion = new SongScrollMotion();
        motion.Request(-120, 3, 120, 960, 10000, 0, TimeSpan.Zero, true);
        Assert.That(motion.Request(-120, 3, 120, 960, 400, 200, TimeSpan.FromMilliseconds(80), false), Is.EqualTo(400));
        Assert.That(motion.IsActive, Is.False);
        motion.Cancel(100);
        Assert.That(motion.Advance(TimeSpan.FromSeconds(1), 1000), Is.EqualTo(100));
        motion.Request(-120, 3, 120, 960, 10000, 100, TimeSpan.FromSeconds(1), true);
        Assert.That(motion.Advance(TimeSpan.FromMilliseconds(1080), 0), Is.Zero);
        Assert.That(motion.IsActive, Is.False);
    }
}
