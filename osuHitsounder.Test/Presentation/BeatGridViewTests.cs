using System.Windows;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class BeatGridViewTests
{
    [Test]
    public void CenteredStartNodeIsClippedAndHitsOnlyVisibleHalf()
    {
        var grid = CreateGrid();
        grid.HorizontalOffset = 0;
        grid.Samples = [new Sample { Timestamp = 0 }];
        Assert.That(grid.GetSampleBounds(0), Is.EqualTo(new Rect(-10, 0, 20, 72)));
        Assert.That(grid.RequestSampleRemoval(0), Is.True);
        Assert.That(grid.RequestSampleRemoval(9), Is.True);
        Assert.That(grid.RequestSampleRemoval(10), Is.False);
        Assert.That(grid.RequestSampleRemoval(-1), Is.False);
        Assert.That(grid.Samples[0].Timestamp, Is.Zero);
    }
    [Test]
    public void PointerSelectsContainingIntervalAndMarkerKeepsTwentyPixelWidth()
    {
        var grid = CreateGrid();

        grid.UpdatePointerPosition(30);

        Assert.That(grid.ActiveInterval, Is.EqualTo(new BeatInterval(250, 500)));
        Assert.That(grid.GetSampleBounds(250), Is.EqualTo(new Rect(0, 0, 20, 72)));
        grid.ContentWidth = 800;
        Assert.That(grid.GetSampleBounds(250), Is.EqualTo(new Rect(50, 0, 20, 72)));
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
    public void RemovalRequestTargetsTopmostNodeUnderPointerAndHonorsGate()
    {
        var first = new Sample { Timestamp = 250 };
        var second = new Sample { Timestamp = 250 };
        var grid = CreateGrid();
        grid.Samples = [first, second];
        Sample? requested = null;
        grid.SampleRemovalRequested += (_, args) => requested = args.Sample;

        Assert.That(grid.RequestSampleRemoval(5), Is.True);
        Assert.That(requested, Is.SameAs(second));
        Assert.That(grid.RequestSampleRemoval(31), Is.False);
        grid.UpdatePointerPosition(30);
        requested = null;
        Assert.That(grid.RequestActiveSampleRemoval(), Is.True);
        Assert.That(requested, Is.SameAs(second));
        grid.CanPlaceSamples = false;
        Assert.That(grid.RequestSampleRemoval(5), Is.False);
        Assert.That(grid.RequestActiveSampleRemoval(), Is.False);
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

    [Test]
    public void PlaybackMarkerUsesSameScrolledTimeScaleAsWaveform()
    {
        var grid = CreateGrid();
        grid.Duration = TimeSpan.FromSeconds(10);
        grid.ContentWidth = 1_000;
        grid.HorizontalOffset = 100;
        grid.Position = TimeSpan.FromSeconds(2);

        Assert.That(grid.GetPlaybackMarkerX(), Is.EqualTo(100).Within(0.001));

        grid.Width = 400;
        grid.ViewportWidth = 400;
        grid.Measure(new Size(400, 72));
        grid.Arrange(new Rect(0, 0, 400, 72));
        grid.ContentWidth = 2_000;
        Assert.That(grid.GetPlaybackMarkerX(), Is.EqualTo(300).Within(0.001));
    }

    [Test]
    public void PlaybackMarkerHidesOutsideViewportAndHandlesInvalidGeometry()
    {
        var grid = CreateGrid();
        grid.Duration = TimeSpan.FromSeconds(10);
        grid.ContentWidth = 1_000;
        grid.HorizontalOffset = 500;
        grid.Position = TimeSpan.FromSeconds(2);
        Assert.That(grid.GetPlaybackMarkerX(), Is.Null);

        grid.Duration = TimeSpan.Zero;
        Assert.That(grid.GetPlaybackMarkerX(), Is.Null);
        grid.Duration = TimeSpan.FromSeconds(10);
        grid.ContentWidth = double.NaN;
        Assert.That(grid.GetPlaybackMarkerX(), Is.Null);
    }

    [Test]
    public void PlaybackPositionChangeDoesNotAlterPlacementSelection()
    {
        var grid = CreateGrid();
        grid.UpdatePointerPosition(30);
        var selected = grid.ActiveInterval;

        grid.Position = TimeSpan.FromSeconds(1);

        Assert.That(grid.ActiveInterval, Is.EqualTo(selected));
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
