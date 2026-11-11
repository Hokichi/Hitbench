using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hitbench.Resources.CustomControls;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class BeatGridDrawingTests
{
    [Test]
    public void WaveformPositionsBeatsOnSuppliedTimeScale()
    {
        Assert.That(Positions(DrawGrid(true)), Is.EqualTo(new[] { 0d, 50d, 100d, 150d, 200d }));
    }

    [TestCase(true, 10d)]
    [TestCase(true, 25d)]
    public void DrawingStaysWithinSuppliedViewportHeight(bool waveform, double height)
    {
        var drawings = DrawGrid(waveform, height);
        Assert.That(drawings, Is.Not.Empty);
        Assert.That(drawings.All(drawing => drawing.Geometry.Bounds.Top >= 0 && drawing.Geometry.Bounds.Bottom <= height), Is.True);
    }

    [Test]
    public void WaveformDrawsOnlySuppliedHorizontalViewport()
    {
        Assert.That(Positions(DrawGrid(true, horizontalOffset: 50, viewportWidth: 100)),
            Is.EqualTo(new[] { 50d, 100d, 150d }));
    }

    [Test]
    public void TrackPositionsIncludeRequestedSubdivisions()
    {
        Assert.That(Positions(DrawGrid(false, subdivision: 4)),
            Is.EqualTo(new[] { 0d, 25d, 50d, 75d, 100d, 125d, 150d, 175d, 200d }));
    }

    [TestCase(18d)]
    [TestCase(24d)]
    [TestCase(36d)]
    [TestCase(72d)]
    [TestCase(144d)]
    public void TrackDrawingStaysWithinSuppliedLane(double height)
    {
        var drawings = DrawGrid(false, height);
        Assert.That(drawings, Is.Not.Empty);
        Assert.That(drawings.All(drawing => drawing.Geometry.Bounds.Top >= 0 && drawing.Geometry.Bounds.Bottom <= height), Is.True);
    }

    private static IReadOnlyList<GeometryDrawing> DrawGrid(bool isWaveform, double height = 72,
        double horizontalOffset = 0, double viewportWidth = 200, int subdivision = 2)
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
            BeatGridDrawing.Draw(context, owner, TimingTestData.Points([0],[60]), subdivision, 2000, 200, horizontalOffset, viewportWidth, height, isWaveform);
        return drawing.Children.OfType<GeometryDrawing>().ToArray();
    }

    private static IEnumerable<double> Positions(IEnumerable<GeometryDrawing> drawings) =>
        drawings.Select(drawing => drawing.Geometry.Bounds.Left + drawing.Geometry.Bounds.Width / 2).Distinct();
}
