using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class BeatGridNodeEditingTests
{
    private static BeatGridView CreateGrid(double height = 36)
    {
        var grid = new BeatGridView { Offsets = [0], Bpms = [60], Subdivision = 4, Duration = TimeSpan.FromSeconds(1),
            ContentWidth = 400, ViewportWidth = 200, HorizontalOffset = 90, CanPlaceSamples = true,
            MarkerBrush = Brushes.Blue, SelectionBrush = Brushes.Yellow, SelectionCueBrush = Brushes.Green,
            HoverBrush = Brushes.Orange, PressedBrush = Brushes.Red };
        UiTestPump.Layout(grid, 200, height);
        return grid;
    }

    [TestCase(18)]
    [TestCase(36)]
    [TestCase(144)]
    public void PreviewChangesDrawingAndHitsWithoutChangingModel(double height)
    {
        var grid = CreateGrid(height); var node = new Sample { Timestamp = 250 };
        grid.Samples = [node]; grid.PreviewTimestamps = new Dictionary<Sample, int> { [node] = 500 };
        Assert.That(grid.GetDisplayedSampleBounds(node), Is.EqualTo(new Rect(100, 0, 20, height)));
        Assert.That(grid.FindSampleAt(new Point(110, height / 2)), Is.SameAs(node));
        Assert.That(grid.FindSampleAt(new Point(10, height / 2)), Is.Null);
        Assert.That(node.Timestamp, Is.EqualTo(250));
    }

    [Test]
    public void HitTestsClipAndPreserveTopmostIdentity()
    {
        var grid = CreateGrid(); var a = new Sample { Timestamp = 250 }; var b = new Sample { Timestamp = 250 };
        grid.Samples = [a, b];
        Assert.That(grid.FindSampleAt(new Point(5, 10)), Is.SameAs(b));
        Assert.That(grid.FindSampleAt(new Point(-1, 10)), Is.Null);
        Assert.That(grid.FindSampleAt(new Point(5, 36)), Is.Null);
        Assert.That(grid.GetSamplesIntersecting(new Rect(0, 0, 15, 20)), Is.EqualTo(new[] { a, b }));
        Assert.That(grid.GetSamplesIntersecting(new Rect(-100, 0, 99, 20)), Is.Empty);
        grid.UpdatePointerPosition(5);
        Assert.That(grid.FindActiveSample(), Is.SameAs(b));
        grid.CanPlaceSamples = false;
        Assert.That(grid.FindSampleAt(new Point(5, 10)), Is.Null);
    }

    [Test]
    public void SelectionPressedAndSuppressedHoverHaveDistinctGeometry()
    {
        var grid = CreateGrid(); var node = new Sample { Timestamp = 250 };
        grid.Samples = [node]; grid.SelectedSamples = [node]; grid.PressedSample = node;
        grid.UpdatePointerPosition(5); grid.SuppressPlacementFeedback = true; grid.PrepareDrawing();
        var drawings = Flatten(grid.ContentDrawing!).OfType<GeometryDrawing>().ToArray();
        Assert.That(drawings.Any(d => ReferenceEquals(d.Brush, Brushes.Orange)), Is.False);
        Assert.That(drawings.Any(d => ReferenceEquals(d.Pen?.Brush, Brushes.Green) && d.Geometry is LineGeometry), Is.True);
        Assert.That(drawings.Any(d => ReferenceEquals(d.Pen?.Brush, Brushes.Red)), Is.True);
        Assert.That(grid.GetSampleBounds(250), Is.EqualTo(new Rect(0, 0, 20, 36)));
        var peer = UIElementAutomationPeer.CreatePeerForElement(grid)!;
        Assert.That(peer.GetItemStatus(), Does.Contain("1"));
        Assert.That(peer.IsEnabled(), Is.True);
        grid.CanPlaceSamples = false;
        Assert.That(peer.IsEnabled(), Is.False);
        Assert.That(grid.PressedSample, Is.Null);
    }

    [Test]
    public void RecyclingClearsLocalPointerAndPressedState()
    {
        var grid = CreateGrid(); var old = new Sample { Timestamp = 250 };
        grid.Samples = [old]; grid.SelectedSamples = [old]; grid.PressedSample = old; grid.UpdatePointerPosition(5);
        grid.DataContext = new Track(); grid.Samples = [new Sample { Timestamp = 250 }];
        Assert.That(grid.PressedSample, Is.Null);
        Assert.That(grid.ActiveInterval, Is.Null);
        Assert.That(UIElementAutomationPeer.CreatePeerForElement(grid)!.GetItemStatus(), Does.Contain("0"));
    }

    private static IEnumerable<Drawing> Flatten(Drawing drawing)
    {
        yield return drawing;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var nested in Flatten(child)) yield return nested;
    }
}
