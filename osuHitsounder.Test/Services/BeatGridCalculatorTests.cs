using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class BeatGridCalculatorTests
{
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
