using Onset.Services;

namespace Onset.Test.Services;

public class PlaybackMarkerClockTests
{
    [TestCase(120, 4.1666667)]
    [TestCase(60, 8.3333333)]
    public void IntervalIsOneHundredTwentiethBeat(int bpm, double interval)
    {
        var clock = new PlaybackMarkerClock([0], [bpm]);
        Assert.That(clock.GetInterval(TimeSpan.Zero).TotalMilliseconds, Is.EqualTo(interval).Within(0.0001));
    }

    [TestCase(10, 8.3333333)]
    [TestCase(12.5, 12.5)]
    [TestCase(100, 100)]
    public void PositionUsesReachedBoundary(double song, double marker)
    {
        var clock = new PlaybackMarkerClock([0], [120]);
        Assert.That(clock.GetPosition(TimeSpan.FromMilliseconds(song), TimeSpan.FromMilliseconds(100)).TotalMilliseconds,
            Is.EqualTo(marker).Within(0.0001));
    }

    [Test]
    public void OffsetsTransitionsAndDuplicateSegmentsAreNormalized()
    {
        var clock = new PlaybackMarkerClock([100, 20, 100], [60, 120, 240]);
        Assert.Multiple(() =>
        {
            Assert.That(clock.GetPosition(TimeSpan.FromMilliseconds(30), TimeSpan.FromSeconds(1)).TotalMilliseconds,
                Is.EqualTo(28.3333333).Within(0.0001));
            Assert.That(clock.GetInterval(TimeSpan.FromMilliseconds(100)).TotalMilliseconds, Is.EqualTo(2.0833333).Within(0.0001));
            Assert.That(clock.GetPosition(TimeSpan.FromMilliseconds(101), TimeSpan.FromSeconds(1)), Is.EqualTo(TimeSpan.FromMilliseconds(100)));
            Assert.That(clock.GetPosition(TimeSpan.Zero, TimeSpan.FromSeconds(1)), Is.EqualTo(TimeSpan.Zero));
        });
    }

    [TestCase(-10)]
    [TestCase(10)]
    public void FirstSegmentExtendsBackwardsAndEndpointIsExact(int offset)
    {
        var clock = new PlaybackMarkerClock([offset], [120]);
        var position = clock.GetPosition(TimeSpan.FromMilliseconds(3), TimeSpan.FromSeconds(1));
        Assert.That(position, Is.InRange(TimeSpan.Zero, TimeSpan.FromMilliseconds(3)));
        Assert.That(clock.GetPosition(TimeSpan.FromMilliseconds(101), TimeSpan.FromMilliseconds(101)), Is.EqualTo(TimeSpan.FromMilliseconds(101)));
    }

    [Test]
    public void InvalidTimingCannotProduceClockValues()
    {
        foreach (var clock in new[] { new PlaybackMarkerClock([], []), new PlaybackMarkerClock([0], []), new PlaybackMarkerClock([0], [0]), new PlaybackMarkerClock([0], [-1]) })
        {
            Assert.That(clock.IsValid, Is.False);
            Assert.Throws<InvalidOperationException>(() => clock.GetInterval(TimeSpan.Zero));
        }
    }
}
