using System.Windows;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class BeatGridViewTests
{
    [Test]
    public void PointerSelectsContainingIntervalAndMarkerKeepsTwentyPixelWidth()
    {
        var grid = CreateGrid();

        grid.UpdatePointerPosition(30);

        Assert.That(grid.ActiveInterval, Is.EqualTo(new BeatInterval(250, 500)));
        Assert.That(grid.GetSampleBounds(250), Is.EqualTo(new Rect(10, 0, 20, 72)));
        grid.ContentWidth = 800;
        Assert.That(grid.GetSampleBounds(250), Is.EqualTo(new Rect(60, 0, 20, 72)));
    }

    [Test]
    public void PlacementEventUsesActiveLeftBoundaryAndHonorsGate()
    {
        var grid = CreateGrid();
        double? requested = null;
        grid.SamplePlacementRequested += (_, args) => requested = args.LeftBoundaryMilliseconds;
        grid.UpdatePointerPosition(30);

        Assert.That(grid.RequestSamplePlacement(), Is.True);
        Assert.That(requested, Is.EqualTo(250));
        grid.CanPlaceSamples = false;
        Assert.That(grid.RequestSamplePlacement(), Is.False);
    }

    [Test]
    public void PointerExitAndInvalidGeometryClearActiveInterval()
    {
        var grid = CreateGrid();
        grid.UpdatePointerPosition(30);
        grid.UpdatePointerPosition(null);
        Assert.That(grid.ActiveInterval, Is.Null);
        grid.Duration = TimeSpan.Zero;
        grid.UpdatePointerPosition(30);
        Assert.That(grid.ActiveInterval, Is.Null);
    }

    [Test]
    public void KeyboardNavigationStartsAtFirstFullyVisibleInterval()
    {
        var grid = CreateGrid();

        Assert.That(grid.MoveActiveInterval(true), Is.True);
        Assert.That(grid.ActiveInterval, Is.EqualTo(new BeatInterval(250, 500)));
    }

    private static BeatGridView CreateGrid()
    {
        var grid = new BeatGridView
        {
            Offsets = [0], Bpms = [60], Subdivision = 4,
            Duration = TimeSpan.FromMilliseconds(2000), ContentWidth = 400,
            HorizontalOffset = 40, ViewportWidth = 200, Width = 200, Height = 72,
            CanPlaceSamples = true, Samples = [new Sample { Timestamp = 250 }]
        };
        grid.Measure(new Size(200, 72));
        grid.Arrange(new Rect(0, 0, 200, 72));
        return grid;
    }
}
