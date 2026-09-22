using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class BeatGridCalculatorTests
{
    [TestCase(200, false, 125)]
    [TestCase(200, true, 250)]
    [TestCase(250, false, 125)]
    [TestCase(250, true, 375)]
    public void FindsStrictAdjacentSubdivision(int start, bool forward, int expected)
    {
        var result = BeatGridCalculator.GetAdjacentPosition([0], [120], 4,
            TimeSpan.FromMilliseconds(start), TimeSpan.FromSeconds(10), forward);
        Assert.That(result, Is.EqualTo(TimeSpan.FromMilliseconds(expected)));
    }

    [TestCase(250, true, 300)]
    [TestCase(300, true, 550)]
    [TestCase(300, false, 250)]
    public void RespectsTimingSegmentBoundary(int start, bool forward, int expected)
    {
        var result = BeatGridCalculator.GetAdjacentPosition([0, 300], [120, 60], 4,
            TimeSpan.FromMilliseconds(start), TimeSpan.FromSeconds(2), forward);
        Assert.That(result, Is.EqualTo(TimeSpan.FromMilliseconds(expected)));
    }

    [Test]
    public void StopsAtDurationAndRejectsInvalidGrid()
    {
        Assert.That(BeatGridCalculator.GetAdjacentPosition([0], [120], 4,
            TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(300), true), Is.Null);
        Assert.That(BeatGridCalculator.GetAdjacentPosition([], [], 4,
            TimeSpan.Zero, TimeSpan.FromSeconds(1), true), Is.Null);
    }

    [Test]
    public void NegativeOffsetSkipsTicksBeforeAudioStart()
    {
        var next = BeatGridCalculator.GetAdjacentPosition([-500], [120], 4,
            TimeSpan.Zero, TimeSpan.FromSeconds(1), true);
        Assert.That(next, Is.EqualTo(TimeSpan.FromMilliseconds(125)));
    }

    [TestCase(1, 500)]
    [TestCase(2, 250)]
    [TestCase(3, 166.6667)]
    [TestCase(4, 125)]
    [TestCase(5, 100)]
    [TestCase(6, 83.3333)]
    [TestCase(8, 62.5)]
    [TestCase(12, 41.6667)]
    [TestCase(16, 31.25)]
    public void SupportsEverySubdivision(int subdivision, double expectedMilliseconds)
    {
        var next = BeatGridCalculator.GetAdjacentPosition([0], [120], subdivision,
            TimeSpan.Zero, TimeSpan.FromSeconds(1), true);
        Assert.That(next!.Value.TotalMilliseconds, Is.EqualTo(expectedMilliseconds).Within(0.001));
    }

    [Test]
    public void RoundedFractionalTicksAlwaysAdvance()
    {
        var current = TimeSpan.Zero;
        for (var i = 0; i < 100; i++)
        {
            var next = BeatGridCalculator.GetAdjacentPosition([0], [137], 16,
                current, TimeSpan.FromSeconds(10), true);
            Assert.That(next, Is.GreaterThan(current));
            current = next!.Value;
        }
        Assert.That(current.TotalMilliseconds, Is.EqualTo(2737.226).Within(0.001));
    }

    [Test]
    public void ExtremelyDenseSubdivisionStillAdvancesByRepresentableTime()
    {
        var next = BeatGridCalculator.GetAdjacentPosition([0], [2_000_000_000], 16,
            TimeSpan.Zero, TimeSpan.FromMilliseconds(1), true);
        Assert.That(next, Is.GreaterThan(TimeSpan.Zero));
    }
    [Test]
    public void GridStartsAtOffsetAndKeepsLargerBeatDivisions()
    {
        var lines = BeatGridCalculator.GetLines([125], [120], 4, 0, 625);
        Assert.That(lines.Select(x => x.Milliseconds), Is.EqualTo(new[] { 125d, 250d, 375d, 500d, 625d }));
        Assert.That(lines.Select(x => x.Division), Is.EqualTo(new[] { 1, 4, 2, 4, 1 }));
    }

    [Test]
    public void NewTimingSegmentStartsAtItsOwnOffset()
    {
        var lines = BeatGridCalculator.GetLines([125, 600], [120, 60], 1, 0, 1800);
        Assert.That(lines.Select(x => x.Milliseconds), Is.EqualTo(new[] { 125d, 600d, 1600d }));
    }

    [TestCase(1, new[] { 1, 1 })]
    [TestCase(2, new[] { 1, 2, 1 })]
    [TestCase(3, new[] { 1, 3, 3, 1 })]
    [TestCase(4, new[] { 1, 4, 2, 4, 1 })]
    [TestCase(5, new[] { 1, 5, 5, 5, 5, 1 })]
    [TestCase(6, new[] { 1, 6, 3, 2, 3, 6, 1 })]
    [TestCase(8, new[] { 1, 8, 4, 8, 2, 8, 4, 8, 1 })]
    [TestCase(12, new[] { 1, 12, 6, 4, 3, 12, 2, 12, 3, 4, 6, 12, 1 })]
    [TestCase(16, new[] { 1, 16, 8, 16, 4, 16, 8, 16, 2, 16, 8, 16, 4, 16, 8, 16, 1 })]
    public void CoincidentDivisionsUseLongestBeatDuration(int subdivision, int[] expected)
    {
        var lines = BeatGridCalculator.GetLines([125], [120], subdivision, 125, 625);
        Assert.That(lines.Select(line => line.Division), Is.EqualTo(expected));
    }

    [Test]
    public void InvalidTimingDoesNotInventGridAtZero()
    {
        Assert.That(BeatGridCalculator.GetLines([], [], 4, 0, 1000), Is.Empty);
        Assert.That(BeatGridCalculator.GetLines([125], [0], 4, 0, 1000), Is.Empty);
        Assert.That(BeatGridCalculator.GetLines([125], [120], 7, 0, 1000), Is.Empty);
    }

    [Test]
    public void NegativeOffsetKeepsPhaseWithoutDrawingBeforeViewport()
    {
        var lines = BeatGridCalculator.GetLines([-125], [120], 2, 0, 700);
        Assert.That(lines.Select(line => line.Milliseconds), Is.EqualTo(new[] { 125d, 375d, 625d }));
        Assert.That(lines.Select(line => line.Division), Is.EqualTo(new[] { 2, 1, 2 }));
    }

    [Test]
    public void ExtremelyDenseGridDoesNotEnumerateMillionsOfLines()
    {
        var lines = BeatGridCalculator.GetLines([0], [2_000_000_000], 16, 0, 1000);
        Assert.That(lines, Is.Empty);
    }
}
