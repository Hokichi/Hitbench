using osuHitsounder.Services.Ui;

namespace osuHitsounder.Test.Presentation;

public class SongTileLayoutTests
{
    [Test]
    public void BaselineFitsFiveColumnsEightRowsAndMinimumFitsThreeRows()
    {
        var layout = new SongTileLayout(1548, 884, 12, 12);
        Assert.Multiple(() =>
        {
            Assert.That(layout.BaselineWidth, Is.EqualTo(300));
            Assert.That(layout.TileHeight, Is.EqualTo(100));
            Assert.That(layout.MinimumViewportWidth, Is.EqualTo(225));
            Assert.That(layout.MinimumViewportHeight, Is.EqualTo(324));
            Assert.That(layout.GetColumns(1548), Is.EqualTo(5));
            Assert.That(layout.GetTileWidth(1548), Is.EqualTo(300));
        });
    }

    [TestCase(1173, 4)]
    [TestCase(936, 3)]
    [TestCase(699, 2)]
    [TestCase(462, 1)]
    public void ThresholdDropsAtEqualityAndReversesAbove(double boundary, int columns)
    {
        var layout = new SongTileLayout(1548, 884, 12, 12);
        foreach (var width in new[] { boundary + .01, boundary, boundary - .01, boundary, boundary + .01 })
        {
            Assert.That(layout.GetColumns(width), Is.EqualTo(width > boundary ? columns + 1 : columns));
            Assert.That(layout.GetTileWidth(width), Is.GreaterThanOrEqualTo(225));
        }
        Assert.That(layout.TileHeight, Is.EqualTo(100));
    }

    [Test]
    public void SmallWidthsRetainSingleMinimumTile()
    {
        var layout = new SongTileLayout(1548, 884, 12, 12);
        Assert.That(layout.GetColumns(100), Is.EqualTo(1));
        Assert.That(layout.GetTileWidth(100), Is.EqualTo(225));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidMeasurementsAreRejected(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SongTileLayout(value, 884, 12, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SongTileLayout(1548, value, 12, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SongTileLayout(1548, 884, 12, 12).GetColumns(value));
    }

    [TestCase(-1)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidGapsAreRejected(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SongTileLayout(1548, 884, value, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SongTileLayout(1548, 884, 12, value));
    }
}
