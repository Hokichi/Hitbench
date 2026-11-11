using Hitbench.Test;
using System.Windows;
using Hitbench.Core.Entities;
using Hitbench.Resources.CustomControls;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA)]
public sealed class BeatGridDrawingCacheTests
{
    [Test]
    public void LargeSelectionIsEnumeratedOnceAndRetainsReferencePriority()
    {
        var nodes = Enumerable.Range(0, 10000).Select(_ => new Sample { Timestamp = 500 }).ToArray();
        var selection = new CountingSampleSelection(nodes[..^1]);
        var grid = new BeatGridView
        {
            Duration = TimeSpan.FromSeconds(1), ContentWidth = 1000, ViewportWidth = 1000,
            CanPlaceSamples = true, Samples = nodes, SelectedSamples = selection
        };
        UiTestPump.Layout(grid, 1000, 60);
        grid.PrepareDrawing();
        Assert.That(grid.FindSampleAt(new Point(500, 30)), Is.SameAs(nodes[^2]));
        grid.HorizontalOffset = 1;
        grid.PrepareDrawing();
        Assert.That(selection.Enumerations, Is.EqualTo(1));
        grid.SelectedSamples = [nodes[^1]];
        Assert.That(grid.FindSampleAt(new Point(499, 30)), Is.SameAs(nodes[^1]));
    }

    [Test]
    public void HoverDoesNotRebuildStableContent()
    {
        var grid = new BeatGridView
        {
            UninheritedPoints = TimingTestData.Points([0], [120]), Duration = TimeSpan.FromSeconds(10),
            ContentWidth = 1000, ViewportWidth = 400, CanPlaceSamples = true
        };
        UiTestPump.Layout(grid, 400, 60);
        grid.PrepareDrawing();
        var before = grid.ContentBuildCount;
        grid.UpdatePointerPosition(80);
        grid.PrepareDrawing();
        Assert.That(grid.ContentBuildCount, Is.EqualTo(before));
    }
}
