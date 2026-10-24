using System.Windows;
using System.Windows.Media;
using Onset.Core.Entities;
using Onset.Resources.CustomControls;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class BeatGridViewTests
{
    [TestCase(18d)]
    [TestCase(36d)]
    [TestCase(144d)]
    public void PlacementAndRemovalRemainFunctionalAcrossLaneHeights(double height)
    {
        var grid = CreateGrid();
        grid.Height = height;
        UiTestPump.Layout(grid, 200, height);
        grid.UpdatePointerPosition(30);
        Assert.That(grid.ActiveInterval, Is.EqualTo(new BeatInterval(250, 500)));
        Assert.That(grid.RequestSamplePlacement(), Is.True);
        var bounds = grid.GetSampleBounds(250);
        Assert.That(grid.RequestSampleRemoval(bounds.Left + bounds.Width / 2), Is.True);
    }

    [Test]
    public void RenderingStyleChangesKeepPlacementContract()
    {
        var grid = CreateGrid();
        grid.MarkerBrush = Brushes.Blue;
        grid.Resources["Brush.Border.Default"] = Brushes.Green;
        grid.PrepareDrawing();
        int? requested = null;
        grid.SampleSelectionRequested += (_, args) => requested = args.Sample.Timestamp;
        grid.UpdatePointerPosition(30);
        Assert.That(grid, Is.TypeOf<BeatGridView>());
        Assert.That(grid.RequestSamplePlacement(), Is.True);
        Assert.That(requested, Is.EqualTo(250));
    }

    [TestCase(60, 30d, 250)]
    [TestCase(123, 0d, 122)]
    [TestCase(60, 0d, 0)]
    public void HoverAndOccupiedSelectionUseSameSnappedTimestamp(int bpm, double pointer, int timestamp)
    {
        var grid = CreateGrid();
        grid.Bpms = [bpm];
        if (timestamp == 0) grid.HorizontalOffset = 0;
        grid.Samples = [new Sample { Timestamp = timestamp }];
        grid.UpdatePointerPosition(pointer);
        double? requested = null;
        grid.SampleSelectionRequested += (_, args) => requested = args.Sample.Timestamp;
        Assert.That(grid.RequestSamplePlacement(), Is.True);
        Assert.That(requested, Is.EqualTo(timestamp).Within(.5));
    }


    [Test]
    public void CenteredStartNodeHitTestingClipsToViewport()
    {
        var grid = CreateGrid();
        grid.HorizontalOffset = 0;
        grid.Samples = [new Sample { Timestamp = 0 }];
        var bounds = grid.GetSampleBounds(0);
        Assert.That(bounds.Left + bounds.Width / 2, Is.Zero);
        Assert.That(grid.RequestSampleRemoval(0), Is.True);
        Assert.That(grid.RequestSampleRemoval(bounds.Right - .01), Is.True);
        Assert.That(grid.RequestSampleRemoval(bounds.Right), Is.False);
        Assert.That(grid.RequestSampleRemoval(-1), Is.False);
        Assert.That(grid.Samples[0].Timestamp, Is.Zero);
    }
    [Test]
    public void PointerSelectsContainingIntervalAndMarkerTracksTimeScale()
    {
        var grid = CreateGrid();
        grid.UpdatePointerPosition(30);
        Assert.That(grid.ActiveInterval, Is.EqualTo(new BeatInterval(250, 500)));
        var bounds = grid.GetSampleBounds(250);
        Assert.That(bounds.Left + bounds.Width / 2, Is.EqualTo(10));
        grid.ContentWidth = 800;
        bounds = grid.GetSampleBounds(250);
        Assert.That(bounds.Left + bounds.Width / 2, Is.EqualTo(60));
    }

    [Test]
    public void PlacementEventUsesActiveLeftBoundaryAndHonorsGate()
    {
        var grid = CreateGrid();
        grid.Samples = [];
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

        var bounds = grid.GetSampleBounds(250);
        Assert.That(grid.RequestSampleRemoval(bounds.Left + bounds.Width / 2), Is.True);
        Assert.That(requested, Is.SameAs(second));
        Assert.That(grid.RequestSampleRemoval(grid.GetSampleBounds(250).Right), Is.False);
        grid.UpdatePointerPosition(30);
        requested = null;
        Assert.That(grid.RequestActiveSampleRemoval(), Is.True);
        Assert.That(requested, Is.SameAs(second));
        grid.CanPlaceSamples = false;
        Assert.That(grid.RequestSampleRemoval(bounds.Left + bounds.Width / 2), Is.False);
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
