using Soundie.Services;

namespace Soundie.Test.Services;

public class BeatGridSnappingTests
{
    [TestCase(187.5, 125)]
    [TestCase(188, 250)]
    [TestCase(0, 0)]
    [TestCase(9999, 9875)]
    [TestCase(10000, 9875)]
    public void NearestBeatHonorsTiesAndExclusiveDuration(double request, int expected) =>
        Assert.That(BeatGridCalculator.GetNearestTimestamp([0], [120], 4, request, 10000), Is.EqualTo(expected));

    [Test]
    public void CollectiveBoundsExcludeUnusableBeats()
    {
        Assert.That(BeatGridCalculator.GetNearestTimestamp([0], [120], 4, 0, 10000, 126, 249), Is.Null);
        Assert.That(BeatGridCalculator.GetNearestTimestamp([0], [120], 4, 9000, 10000, 125, 375), Is.EqualTo(375));
        Assert.That(BeatGridCalculator.GetNearestTimestamp([0], [120], 4, 0, 10000, 125, 375), Is.EqualTo(125));
    }

    [Test]
    public void TimingSegmentsOffsetsAndFractionalBeatsRemainUsable()
    {
        Assert.That(BeatGridCalculator.GetNearestTimestamp([0], [123], 4, 120, 10000), Is.EqualTo(122));
        Assert.That(BeatGridCalculator.GetNearestTimestamp([0, 700], [120, 60], 4, 690, 10000), Is.EqualTo(700));
        Assert.That(BeatGridCalculator.GetNearestTimestamp([1000], [120], 4, 0, 10000), Is.EqualTo(1000));
        Assert.That(BeatGridCalculator.GetNearestTimestamp([700, 0, 700], [120, 120, 60], 4, 950, 10000), Is.EqualTo(950));
        Assert.That(BeatGridCalculator.GetNearestTimestamp([-100], [120], 4, 0, 10000), Is.EqualTo(25));
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void NonfiniteRequestsAreRejected(double request) =>
        Assert.That(BeatGridCalculator.GetNearestTimestamp([0], [120], 4, request, 10000), Is.Null);

    [Test]
    public void InvalidTimingAndRangesAreRejectedWithoutOverflow()
    {
        Assert.That(BeatGridCalculator.GetNearestTimestamp([], [], 4, 0, 10000), Is.Null);
        Assert.That(BeatGridCalculator.GetNearestTimestamp([0], [0], 4, 0, 10000), Is.Null);
        Assert.That(BeatGridCalculator.GetNearestTimestamp([0], [120], 7, 0, 10000), Is.Null);
        Assert.That(BeatGridCalculator.GetNearestTimestamp([0], [120], 4, 0, double.NaN), Is.Null);
        Assert.That(BeatGridCalculator.GetNearestTimestamp([0], [120], 4, 0, 0), Is.Null);
        Assert.That(BeatGridCalculator.GetNearestTimestamp([0], [120], 4, 0, 10000, 500, 100), Is.Null);
        Assert.That(BeatGridCalculator.GetNearestTimestamp([0], [120], 4, double.MaxValue, double.MaxValue), Is.InRange(0, int.MaxValue));
    }
}
