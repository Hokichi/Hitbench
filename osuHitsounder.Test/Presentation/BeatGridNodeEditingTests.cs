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
    [Test]
    public void DraggedSamplesAreHiddenFromNodeInteraction()
    {
        var grid = CreateGrid();
        var moving = new Sample { Timestamp = 250 };
        var stationary = new Sample { Timestamp = 500 };
        grid.Samples = [moving, stationary]; grid.SelectedSamples = [moving];
        grid.IsNodeDragActive = true;
        grid.SetActivePosition(TimeSpan.FromMilliseconds(250));
        Assert.That(grid.GetDisplayedSampleBounds(moving).IsEmpty, Is.True);
        Assert.That(grid.FindSampleAt(new Point(10, grid.ActualHeight / 2)), Is.Null);
        Assert.That(grid.GetSamplesIntersecting(new Rect(0, 0, 200, 36)), Is.EqualTo(new[] { stationary }));
        Assert.That(grid.FindActiveSample(), Is.Null);
        Assert.That(grid.RequestSampleRemoval(10), Is.False);
        Assert.That(grid.RequestActiveSampleRemoval(), Is.False);
        Assert.That(grid.FindSampleAt(new Point(110, grid.ActualHeight / 2)), Is.SameAs(stationary));
        grid.IsNodeDragActive = false;
        Assert.That(grid.FindSampleAt(new Point(10, grid.ActualHeight / 2)), Is.SameAs(moving));
        Assert.That(grid.FindActiveSample(), Is.SameAs(moving));
        Assert.That(moving.Timestamp, Is.EqualTo(250));
    }

    [Test]
    public void ActiveSampleActionsDoNotRequireMeasuredNodeGeometry()
    {
        var node = new Sample { Timestamp = 250 };
        var grid = new BeatGridView { Offsets = [0], Bpms = [60], Subdivision = 4,
            Duration = TimeSpan.FromSeconds(1), CanPlaceSamples = true, Samples = [node], SelectedSamples = [node] };
        grid.SetActivePosition(TimeSpan.FromMilliseconds(250));
        Assert.That(grid.FindActiveSample(), Is.SameAs(node));
        grid.IsNodeDragActive = true;
        Assert.That(grid.FindActiveSample(), Is.Null);
        Assert.That(grid.RequestActiveSampleRemoval(), Is.False);
        grid.IsNodeDragActive = false;
        Sample? removed = null;
        grid.SampleRemovalRequested += (_, args) => removed = args.Sample;
        Assert.That(grid.RequestActiveSampleRemoval(), Is.True);
        Assert.That(removed, Is.SameAs(node));
    }

    [TestCase(-1, 50d)]
    [TestCase(250, 5d)]
    [TestCase(275, 20d)]
    [TestCase(400, 50d)]
    public void SelectionModeNeverAddsHoverPlacementFeedback(int timestamp, double pointer)
    {
        var grid = CreateGrid();
        grid.AllowNodeCreation = false;
        grid.Samples = timestamp < 0 ? [] : [new Sample { Timestamp = timestamp }];
        grid.PrepareDrawing();
        var idleCommands = CountDrawingCommands(grid.ContentDrawing!);
        grid.UpdatePointerPosition(pointer);
        grid.PrepareDrawing();
        Assert.That(CountDrawingCommands(grid.ContentDrawing!), Is.EqualTo(idleCommands));
        if (grid.Samples.Count > 0)
        {
            var node = grid.Samples[0];
            var bounds = grid.GetDisplayedSampleBounds(node);
            Assert.That(grid.FindSampleAt(new Point(bounds.Left + bounds.Width / 2, grid.ActualHeight / 2)), Is.SameAs(node));
            Sample? selected = null;
            grid.SampleSelectionRequested += (_, args) => selected = args.Sample;
            grid.SetActivePosition(TimeSpan.FromMilliseconds(timestamp));
            Assert.That(grid.RequestSamplePlacement(), Is.True);
            Assert.That(selected, Is.SameAs(node));
        }
        else Assert.That(grid.RequestSamplePlacement(), Is.False);
    }

    [Test]
    public void ModeChangeInvalidatesStationaryPointerPlacementFeedback()
    {
        var grid = CreateGrid();
        grid.Samples = [new Sample { Timestamp = 400 }];
        grid.PrepareDrawing();
        var idleCommands = CountDrawingCommands(grid.ContentDrawing!);
        grid.UpdatePointerPosition(50);
        for (var repeat = 0; repeat < 3; repeat++)
        {
            grid.AllowNodeCreation = true;
            grid.PrepareDrawing();
            Assert.That(CountDrawingCommands(grid.ContentDrawing!), Is.GreaterThan(idleCommands));
            var additionDrawing = grid.ContentDrawing;
            grid.AllowNodeCreation = false;
            grid.PrepareDrawing();
            Assert.That(grid.ContentDrawing, Is.Not.SameAs(additionDrawing));
            Assert.That(CountDrawingCommands(grid.ContentDrawing!), Is.EqualTo(idleCommands));
        }
        grid.HorizontalOffset = 80;
        grid.ZoomFactor = 16;
        grid.UpdatePointerPosition(null); grid.PrepareDrawing();
        idleCommands = CountDrawingCommands(grid.ContentDrawing!);
        grid.UpdatePointerPosition(60); grid.PrepareDrawing();
        Assert.That(CountDrawingCommands(grid.ContentDrawing!), Is.EqualTo(idleCommands));
        grid.DataContext = new Track(); grid.PrepareDrawing();
        Assert.That(grid.ActiveInterval, Is.Null);
        Assert.That(CountDrawingCommands(grid.ContentDrawing!), Is.EqualTo(idleCommands));
        grid.UpdatePointerPosition(60); grid.PrepareDrawing();
        Assert.That(CountDrawingCommands(grid.ContentDrawing!), Is.EqualTo(idleCommands));
    }

    private static int CountDrawingCommands(Drawing drawing) => drawing is DrawingGroup group
        ? group.Children.Sum(CountDrawingCommands) : 1;

    [TestCase(8)]
    [TestCase(16)]
    [TestCase(32)]
    [TestCase(64)]
    [TestCase(128)]
    public void ZoomScaledHitTestingUsesTimestampCenteredFootprints(double zoom)
    {
        var grid = CreateGrid(); grid.ZoomFactor = zoom;
        var node = new Sample { Timestamp = 500 }; grid.Samples = [node];
        var bounds = grid.GetSampleBounds(node.Timestamp);
        Assert.That(bounds.Left + bounds.Width / 2, Is.EqualTo(110));
        Assert.That(grid.FindSampleAt(new Point(bounds.Right - .01, 10)), Is.SameAs(node));
        Assert.That(grid.FindSampleAt(new Point(bounds.Right, 10)), Is.Null);
        Assert.That(grid.RequestSampleRemoval(bounds.Right), Is.False);
        Assert.That(grid.GetSamplesIntersecting(new Rect(bounds.Right + .1, 0, 1, 20)), Is.Empty);
        Assert.That(node.Timestamp, Is.EqualTo(500));
    }
    [TestCase(18d)]
    [TestCase(36d)]
    [TestCase(144d)]
    public void LanePaddingDoesNotHitNode(double height)
    {
        var grid = CreateGrid(height); var node = new Sample { Timestamp = 250 }; grid.Samples = [node];
        Assert.That(grid.FindSampleAt(new Point(10, height / 2)), Is.SameAs(node));
        Assert.That(grid.FindSampleAt(new Point(10, height / 8)), Is.Null);
        Assert.That(grid.GetSamplesIntersecting(new Rect(9, height / 8, 2, 1)), Is.Empty);
    }
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
    public void DragStateRestoresCommittedHitPositionWithoutChangingModel(double height)
    {
        var grid = CreateGrid(height); var node = new Sample { Timestamp = 250 };
        grid.Samples = [node]; grid.SelectedSamples = [node]; grid.IsNodeDragActive = true;
        Assert.That(grid.GetDisplayedSampleBounds(node).IsEmpty, Is.True);
        Assert.That(grid.FindSampleAt(new Point(10, height / 2)), Is.Null);
        grid.IsNodeDragActive = false;
        Assert.That(grid.FindSampleAt(new Point(10, height / 2)), Is.SameAs(node));
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
    public void SelectionAndPressedStateKeepHitTestingAndAutomation()
    {
        var grid = CreateGrid(); var node = new Sample { Timestamp = 250 };
        grid.Samples = [node]; grid.SelectedSamples = [node]; grid.PressedSample = node;
        grid.UpdatePointerPosition(5); grid.SuppressPlacementFeedback = true; grid.PrepareDrawing();
        var bounds = grid.GetSampleBounds(250);
        Assert.That(grid.FindSampleAt(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)), Is.SameAs(node));
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

}
