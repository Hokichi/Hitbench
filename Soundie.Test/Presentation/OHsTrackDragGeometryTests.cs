using System.Windows;
using Soundie.Resources.Helpers;

namespace Soundie.Test.Presentation;

public sealed class OHsTrackDragGeometryTests
{
    [TestCase(3, 5, false)]
    [TestCase(4, 0, true)]
    [TestCase(0, 6, true)]
    [TestCase(-4, 0, true)]
    [TestCase(double.NaN, 6, false)]
    [TestCase(4, double.PositiveInfinity, false)]
    public void ThresholdUsesEitherAxisAndRejectsNonfiniteInput(double x, double y, bool expected) =>
        Assert.That(OHsTrackDragGeometry.CrossedThreshold(new Point(), new Point(x, y), 4, 6), Is.EqualTo(expected));

    [TestCase(10, 0)]
    [TestCase(30, 1)]
    [TestCase(80, 1)]
    [TestCase(130, 2)]
    [TestCase(180, 3)]
    public void SlotsUseMixedExplicitHeights(double y, int slot) =>
        Assert.That(OHsTrackDragGeometry.FindInsertionSlot(new Point(10, y), new Rect(0, 0, 300, 200),
            [(0, new Rect(0, 0, 300, 40)), (1, new Rect(0, 40, 300, 100)), (2, new Rect(0, 140, 300, 60))], 3), Is.EqualTo(slot));

    [TestCase(1, 4, 4, 3)]
    [TestCase(3, 0, 4, 0)]
    [TestCase(1, 1, 4, 1)]
    [TestCase(1, 2, 4, 1)]
    [TestCase(1, 3, 4, 2)]
    [TestCase(2, 1, 4, 1)]
    [TestCase(1, 5, 4, null)]
    [TestCase(-1, 0, 4, null)]
    [TestCase(4, 0, 4, null)]
    [TestCase(0, -1, 4, null)]
    [TestCase(0, 0, 0, null)]
    public void FinalIndexAccountsForSourceRemoval(int source, int slot, int count, int? expected) =>
        Assert.That(OHsTrackDragGeometry.ToFinalIndex(source, slot, count), Is.EqualTo(expected));

    [Test]
    public void ExistingGapKeepsStationaryDestinationStable()
    {
        var viewport = new Rect(0, 0, 300, 240);
        (int, Rect)[] rows = [(0, new Rect(0, 0, 300, 40)), (1, new Rect(0, 80, 300, 100)), (2, new Rect(0, 180, 300, 60))];
        for (var i = 0; i < 4; i++)
            Assert.That(OHsTrackDragGeometry.FindInsertionSlot(new Point(10, 60), viewport, rows, 3, 1, new Rect(0, 40, 300, 40)), Is.EqualTo(1));
        Assert.That(OHsTrackDragGeometry.FindInsertionSlot(new Point(10, 170), viewport, rows, 3, 1, new Rect(0, 40, 300, 40)), Is.EqualTo(2));
    }

    [Test]
    public void PartialRealizationUsesLiveIndices()
    {
        var viewport = new Rect(0, 0, 300, 100);
        Assert.That(OHsTrackDragGeometry.FindInsertionSlot(new Point(10, 5), viewport,
            [(20, new Rect(0, -10, 300, 40)), (21, new Rect(0, 30, 300, 100))], 100), Is.EqualTo(20));
        Assert.That(OHsTrackDragGeometry.FindInsertionSlot(new Point(10, 95), viewport,
            [(20, new Rect(0, -10, 300, 40)), (21, new Rect(0, 30, 300, 100))], 100), Is.EqualTo(22));
    }

    [TestCase(-1, 10)]
    [TestCase(301, 10)]
    [TestCase(10, -1)]
    [TestCase(10, 201)]
    [TestCase(double.NaN, 10)]
    [TestCase(10, double.PositiveInfinity)]
    public void InvalidOrOutsidePointerHasNoSlot(double x, double y) =>
        Assert.That(OHsTrackDragGeometry.FindInsertionSlot(new Point(x, y), new Rect(0, 0, 300, 200), [(0, new Rect(0, 0, 300, 40))], 1), Is.Null);

    [Test]
    public void EmptyUnarrangedAndInvalidRowsHaveNoSlot()
    {
        var viewport = new Rect(0, 0, 300, 200);
        Assert.That(OHsTrackDragGeometry.FindInsertionSlot(new Point(10, 10), viewport, [], 1), Is.Null);
        Assert.That(OHsTrackDragGeometry.FindInsertionSlot(new Point(10, 10), viewport, [(0, new Rect())], 1), Is.Null);
        Assert.That(OHsTrackDragGeometry.FindInsertionSlot(new Point(10, 10), viewport, [(2, new Rect(0, 0, 300, 40))], 1), Is.Null);
    }
}
