using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class BeatGridDrawingTests
{
    [Test]
    public void WaveformUsesThirtyAndTwentyUnitBars()
    {
        var drawings = DrawGrid(isWaveform: true);
        Assert.That(((LineGeometry)LineAt(drawings, 0).Geometry).StartPoint.Y, Is.EqualTo(42));
        Assert.That(((LineGeometry)LineAt(drawings, 50).Geometry).StartPoint.Y, Is.EqualTo(52));
    }

    [TestCase(true, 10d, 0d, 0d)]
    [TestCase(true, 25d, 0d, 5d)]
    [TestCase(false, 72d, 0d, 0d)]
    public void BarsClipOrFillAvailableHeight(bool waveform, double height, double wholeStart, double otherStart)
    {
        var drawings = DrawGrid(waveform, height);
        var whole = (LineGeometry)LineAt(drawings, 0).Geometry;
        var other = (LineGeometry)LineAt(drawings, 50).Geometry;
        Assert.That(whole.StartPoint.Y, Is.EqualTo(wholeStart));
        Assert.That(other.StartPoint.Y, Is.EqualTo(otherStart));
        Assert.That(whole.EndPoint.Y, Is.EqualTo(height));
        Assert.That(other.EndPoint.Y, Is.EqualTo(height));
    }

    [Test]
    public void WaveformBottomAlignsThirtyUnitWholeBarsAndTwentyUnitOtherBars()
    {
        var drawings = DrawGrid(isWaveform: true);

        Assert.Multiple(() =>
        {
            Assert.That(((LineGeometry)LineAt(drawings, 0).Geometry).StartPoint.Y, Is.EqualTo(42));
            Assert.That(((LineGeometry)LineAt(drawings, 50).Geometry).StartPoint.Y, Is.EqualTo(52));
            Assert.That(((LineGeometry)LineAt(drawings, 0).Geometry).EndPoint.Y, Is.EqualTo(72));
            Assert.That(((LineGeometry)LineAt(drawings, 50).Geometry).EndPoint.Y, Is.EqualTo(72));
        });
    }

    [Test]
    public void TrackUsesWhiteForWholeBarsAndLightGrayForOtherBars()
    {
        var drawings = DrawGrid(isWaveform: false);

        Assert.Multiple(() =>
        {
            Assert.That(LineAt(drawings, 0).Pen!.Brush, Is.SameAs(Brushes.White));
            Assert.That(LineAt(drawings, 50).Pen!.Brush, Is.SameAs(Brushes.LightGray));
        });
    }

    private static IReadOnlyList<GeometryDrawing> DrawGrid(bool isWaveform, double height = 72)
    {
        var owner = new Border();
        owner.Resources.Add("Brush.Beat.Whole", Brushes.White);
        owner.Resources.Add("Brush.Beat.Half", Brushes.Red);
        owner.Resources.Add("Brush.Beat.Fourth", Brushes.Blue);
        owner.Resources.Add("Brush.Beat.Triplet", Brushes.Purple);
        owner.Resources.Add("Brush.Beat.FifthEighth", Brushes.Yellow);
        owner.Resources.Add("Brush.Beat.Sixteenth", Brushes.LightGray);
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
            BeatGridDrawing.Draw(context, owner, [0], [60], 2, 2000, 200, 0, 200, height, isWaveform);
        return drawing.Children.OfType<GeometryDrawing>().ToArray();
    }

    private static GeometryDrawing LineAt(IEnumerable<GeometryDrawing> drawings, double x) =>
        drawings.Single(drawing => Math.Abs(((LineGeometry)drawing.Geometry).StartPoint.X - x) < 0.001);
}
