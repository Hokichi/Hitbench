using System.Windows.Controls;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Soundie.Core.Entities;
using Soundie.Resources.CustomControls;
using Soundie.Services;
using Soundie.ViewModels.Shell;
using Soundie.Views.Pages;

namespace Soundie.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationNodeInteractionTests
{
    [Test]
    public void SelectedGroupAdornerDrawsEveryNode()
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grids = Descendants<BeatGridView>(editor.Page).ToArray();
        foreach (var grid in grids) { grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0; }
        var other = new Sample { Timestamp = 1500, FileName = "b.wav" };
        editor.Vm.Tracks[1].Samples.Add(other); editor.Vm.SetNodeSelection([editor.Node, other]);
        editor.Page.BeginNodePointer(grids[0], new Point(100, editor.Vm.TrackHeight / 2), new Point());
        editor.Page.UpdateNodePointer(new Point(200, 200), grids[0], new Point(125, editor.Vm.TrackHeight / 2));
        var list = (ListBox)((Soundie.Views.Pages.OHsWorkstationTrackList)editor.Page.FindName("TrackListComponent")).FindName("TrackList");
        var adorner = AdornerLayer.GetAdornerLayer(list)!.GetAdorners(list)!.Single();
        UiTestPump.Layout(adorner, list.ActualWidth, list.ActualHeight);
        var drawing = VisualTreeHelper.GetDrawing(adorner);
        Assert.That(drawing, Is.Not.Null);
        Assert.That(NodeDrawings(drawing!).Select(d => d.Geometry.Bounds).Distinct().Count(), Is.EqualTo(2));
        Assert.That(grids[0].GetDisplayedSampleBounds(editor.Node).IsEmpty, Is.True);
        Assert.That(grids[1].GetDisplayedSampleBounds(other).IsEmpty, Is.True);
        Assert.That(editor.Vm.CanUndo, Is.False);
        editor.Page.CancelNodePointer(); UiTestPump.Run(editor.Page.DeactivateAsync());
    }

    private static IEnumerable<GeometryDrawing> NodeDrawings(Drawing drawing)
    {
        if (drawing is GeometryDrawing geometry) yield return geometry;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var geometryChild in NodeDrawings(child)) yield return geometryChild;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InvalidGroupTargetClearsPlaceholderAndRecovers(bool recover)
    {
        var editor = CreateEditor(6); using var view = editor.View;
        editor.Vm.ToggleTracksCollapsedCommand.Execute(null); view.UpdateLayout();
        var grids = Descendants<BeatGridView>(editor.Page).ToDictionary(g => (Soundie.Core.Entities.Track)g.DataContext);
        foreach (var grid in grids.Values) { grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0; }
        var other = new Sample { Timestamp = 1500, FileName = "b.wav" }; editor.Vm.Tracks[3].Samples.Add(other);
        editor.Vm.SetNodeSelection([editor.Node, other]);
        var source = grids[editor.Vm.Tracks[3]]; var valid = grids[editor.Vm.Tracks[5]]; var invalid = grids[editor.Vm.Tracks[0]];
        editor.Page.BeginNodePointer(source, new Point(150, source.ActualHeight / 2), new Point());
        editor.Page.UpdateNodePointer(new Point(30, 100), valid, new Point(175, valid.ActualHeight / 2));
        Assert.That(valid.DragPreviewTimestamps, Is.EqualTo(new[] { 1750 }));
        editor.Page.UpdateNodePointer(new Point(30, 120), invalid, new Point(175, invalid.ActualHeight / 2));
        Assert.That(valid.DragPreviewTimestamps, Is.Null); Assert.That(invalid.DragPreviewTimestamps, Is.Null);
        Assert.That(editor.Vm.IsNodeDragActive, Is.True);
        if (recover) editor.Page.UpdateNodePointer(new Point(30, 100), valid, new Point(175, valid.ActualHeight / 2));
        var complete = editor.Page.CompleteNodePointerAsync(); UiTestPump.Run(complete);
        Assert.That(complete.Result, Is.EqualTo(recover));
        Assert.That(editor.Vm.Tracks[recover ? 2 : 0].Samples.Contains(editor.Node), Is.True);
        Assert.That(editor.Vm.Tracks[recover ? 5 : 3].Samples.Contains(other), Is.True);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(recover ? 1250 : 1000));
        Assert.That(other.Timestamp, Is.EqualTo(recover ? 1750 : 1500));
        Assert.That(valid.DragPreviewTimestamps, Is.Null);
        Assert.That(AdornerLayer.GetAdornerLayer((ListBox)((Soundie.Views.Pages.OHsWorkstationTrackList)editor.Page.FindName("TrackListComponent")).FindName("TrackList"))!.GetAdorners((ListBox)((Soundie.Views.Pages.OHsWorkstationTrackList)editor.Page.FindName("TrackListComponent")).FindName("TrackList")), Is.Null);
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }

    [TestCase(FlowDirection.LeftToRight, false)]
    [TestCase(FlowDirection.LeftToRight, true)]
    [TestCase(FlowDirection.RightToLeft, false)]
    [TestCase(FlowDirection.RightToLeft, true)]
    public void SelectedGroupGeometryFollowsAnchorAndFlowDirection(FlowDirection direction, bool laterAnchor)
    {
        var editor = CreateEditor(); using var view = editor.View;
        editor.Page.FlowDirection = direction; view.UpdateLayout();
        var grids = Descendants<BeatGridView>(editor.Page).ToArray();
        foreach (var grid in grids) { grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0; }
        var other = new Sample { Timestamp = 1000, FileName = "b.wav" }; editor.Vm.Tracks[1].Samples.Add(other);
        var sameTrack = new Sample { Timestamp = 1500, FileName = "a.wav" }; editor.Vm.Tracks[0].Samples.Add(sameTrack);
        editor.Vm.SetNodeSelection([editor.Node, sameTrack, other]);
        var source = grids[laterAnchor ? 1 : 0]; var anchor = laterAnchor ? other : editor.Node;
        editor.Page.BeginNodePointer(source, new Point(100, source.ActualHeight / 2), new Point());
        editor.Page.UpdateNodePointer(new Point(200, 200), source, new Point(125, source.ActualHeight / 2));
        var list = (ListBox)((Soundie.Views.Pages.OHsWorkstationTrackList)editor.Page.FindName("TrackListComponent")).FindName("TrackList");
        var adorner = (OHsSampleNodeDragAdorner)AdornerLayer.GetAdornerLayer(list)!.GetAdorners(list)!.Single();
        Assert.That(adorner.NodeBounds, Has.Count.EqualTo(3));
        var anchorCenter = source.TranslatePoint(Center(source.GetSampleBounds(anchor.Timestamp)), list);
        var expected = new[] { grids[0].TranslatePoint(Center(grids[0].GetSampleBounds(1000)), list),
            grids[0].TranslatePoint(Center(grids[0].GetSampleBounds(1500)), list),
            grids[1].TranslatePoint(Center(grids[1].GetSampleBounds(1000)), list) };
        foreach (var (actual, target) in adorner.NodeBounds.Select(Center).Zip(expected.Select(p => new Point(p.X - anchorCenter.X, p.Y - anchorCenter.Y))))
        {
            Assert.That(actual.X, Is.EqualTo(target.X).Within(0.000001));
            Assert.That(actual.Y, Is.EqualTo(target.Y).Within(0.000001));
        }
        editor.Page.CancelNodePointer(); UiTestPump.Run(editor.Page.DeactivateAsync());
    }

    [Test]
    public void GroupAdornerRefreshesWithZoomAndTrackSizing()
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grids = Descendants<BeatGridView>(editor.Page).ToArray();
        foreach (var grid in grids) { grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0; }
        var other = new Sample { Timestamp = 1500, FileName = "b.wav" }; editor.Vm.Tracks[1].Samples.Add(other);
        editor.Vm.SetNodeSelection([editor.Node, other]);
        editor.Page.BeginNodePointer(grids[0], new Point(100, grids[0].ActualHeight / 2), new Point());
        editor.Page.UpdateNodePointer(new Point(200, 200), grids[0], new Point(125, grids[0].ActualHeight / 2));
        var list = (ListBox)((Soundie.Views.Pages.OHsWorkstationTrackList)editor.Page.FindName("TrackListComponent")).FindName("TrackList");
        var adorner = (OHsSampleNodeDragAdorner)AdornerLayer.GetAdornerLayer(list)!.GetAdorners(list)!.Single();
        var before = Center(adorner.NodeBounds[1]) - Center(adorner.NodeBounds[0]);
        editor.Vm.ZoomInCommand.Execute(null);
        foreach (var grid in grids) grid.ContentWidth = 2000;
        editor.Vm.IncreaseTrackHeightCommand.Execute(null); view.UpdateLayout();
        var after = Center(adorner.NodeBounds[1]) - Center(adorner.NodeBounds[0]);
        Assert.That(after.X, Is.EqualTo(before.X * 2).Within(.001));
        var expectedY = grids[1].TranslatePoint(new Point(), list).Y - grids[0].TranslatePoint(new Point(), list).Y;
        Assert.That(after.Y, Is.EqualTo(expectedY).Within(.001));
        Assert.That(after.Y, Is.GreaterThan(before.Y));
        editor.Page.CancelNodePointer(); UiTestPump.Run(editor.Page.DeactivateAsync());
    }

    [Test]
    public void VirtualizedSelectionRemainsInAdorner()
    {
        var editor = CreateEditor(100); using var view = editor.View;
        var grid = Descendants<BeatGridView>(editor.Page).First();
        grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0;
        var distant = new Sample { Timestamp = 1500, FileName = "b.wav" }; editor.Vm.Tracks[99].Samples.Add(distant);
        var list = (ListBox)((Soundie.Views.Pages.OHsWorkstationTrackList)editor.Page.FindName("TrackListComponent")).FindName("TrackList");
        Assert.That(list.ItemContainerGenerator.ContainerFromIndex(99), Is.Null);
        editor.Vm.SetNodeSelection([editor.Node, distant]);
        editor.Page.BeginNodePointer(grid, new Point(100, grid.ActualHeight / 2), new Point());
        editor.Page.UpdateNodePointer(new Point(200, 200), grid, new Point(125, grid.ActualHeight / 2));
        var adorner = (OHsSampleNodeDragAdorner)AdornerLayer.GetAdornerLayer(list)!.GetAdorners(list)!.Single();
        Assert.That(adorner.NodeBounds, Has.Count.EqualTo(2));
        var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        var pitch = container.ActualHeight + container.Margin.Top + container.Margin.Bottom;
        Assert.That(Center(adorner.NodeBounds[1]).Y - Center(adorner.NodeBounds[0]).Y, Is.EqualTo(99 * pitch).Within(.001));
        Assert.That(list.ItemContainerGenerator.ContainerFromIndex(99), Is.Null);
        editor.Page.CancelNodePointer(); UiTestPump.Run(editor.Page.DeactivateAsync());
    }

    private static Point Center(Rect bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

    [TestCase(123, 1, 0, 1000d, 0d, false)]
    [TestCase(123, 2, 0, 1000d, 0d, false)]
    [TestCase(123, 3, 0, 1000d, 0d, false)]
    [TestCase(123, 4, 0, 1000d, 0d, false)]
    [TestCase(123, 5, 0, 1000d, 0d, false)]
    [TestCase(123, 6, 0, 1000d, 0d, false)]
    [TestCase(123, 8, 0, 1000d, 0d, false)]
    [TestCase(123, 12, 0, 1000d, 0d, false)]
    [TestCase(123, 16, 0, 1000d, 0d, false)]
    [TestCase(120, 4, 0, 1000d, 0d, false)]
    [TestCase(123, 4, 73, 2000d, 20d, false)]
    [TestCase(137, 12, 700, 3000d, 200d, false)]
    [TestCase(123, 4, 0, 4000d, 0d, true)]
    public void NewNodePlacementMatchesCapturedBeat(int bpm, int subdivision, int offset,
        double contentWidth, double horizontalOffset, bool occupyPreviousBeat)
    {
        var editor = CreateEditor(bpm: bpm, offset: offset); using var view = editor.View;
        try
        {
            var vm = editor.Vm; var track = vm.Tracks[1];
            vm.SelectedSubdivisionIndex = Array.IndexOf(new[] { 1, 2, 3, 4, 5, 6, 8, 12, 16 }, subdivision);
            var grid = Descendants<BeatGridView>(editor.Page).Single(g => ReferenceEquals(g.DataContext, track));
            grid.Offsets = [offset]; grid.Bpms = [bpm]; grid.Subdivision = subdivision;
            grid.ContentWidth = contentWidth; grid.ViewportWidth = 500; grid.HorizontalOffset = horizontalOffset;
            if (occupyPreviousBeat) track.Samples.Add(new Sample { Timestamp = offset, FileName = "b.wav" });
            var initialCount = track.Samples.Count;
            var step = 60000d / bpm / subdivision;
            var pointer = new Point((offset + 1.5 * step) / grid.Duration.TotalMilliseconds * contentWidth - horizontalOffset,
                grid.ActualHeight / 2);
            grid.UpdatePointerPosition(pointer.X);
            Assert.That(grid.ActiveInterval, Is.Not.Null);
            var captured = grid.ActiveInterval!.Value;
            Assert.That(captured.StartMilliseconds, Is.EqualTo(offset + step).Within(1e-9));
            var expected = (int)Math.Round(captured.StartMilliseconds, MidpointRounding.AwayFromZero);
            Assert.That(editor.Page.BeginNodePointer(grid, pointer, new Point()), Is.True);
            var completion = editor.Page.CompleteNodePointerAsync(); UiTestPump.Run(completion);
            Assert.That(completion.Result, Is.True);
            var placed = vm.SelectedSamples.Single();
            Assert.That(placed.Timestamp, Is.EqualTo(expected));
            Assert.That(track.Samples.Count, Is.EqualTo(initialCount + 1));
            Assert.That(vm.CanUndo, Is.True);
            vm.UndoCommand.Execute(null);
            Assert.That(track.Samples.Contains(placed), Is.False);
            Assert.That(track.Samples.Count, Is.EqualTo(initialCount));
            Assert.That(vm.CanUndo, Is.False);
            vm.RedoCommand.Execute(null);
            Assert.That(track.Samples.Contains(placed), Is.True);
            Assert.That(placed.Timestamp, Is.EqualTo(expected));
        }
        finally { UiTestPump.Run(editor.Page.DeactivateAsync()); }
    }

    [Test]
    public void BlankClickInOccupiedIntervalDoesNotReselectOnRelease()
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grid = Descendants<BeatGridView>(editor.Page).First();
        grid.ContentWidth = 4000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0;
        editor.Vm.SelectNode(editor.Vm.Tracks[0], editor.Node, false);
        var blankX = grid.GetSampleBounds(editor.Node.Timestamp).Right + 1;
        Assert.That(editor.Page.BeginNodePointer(grid, new Point(blankX, editor.Vm.TrackHeight / 2), new Point()), Is.True);
        UiTestPump.Run(editor.Page.CompleteNodePointerAsync());
        Assert.That(editor.Vm.SelectedSamples, Is.Empty);
        grid.UpdatePointerPosition(blankX);
        Assert.That(grid.RequestPointerSamplePlacement(), Is.True);
        Assert.That(editor.Vm.SelectedSamples, Is.Empty);
        Assert.That(editor.Vm.Tracks[0].Samples, Has.Count.EqualTo(1));
        Assert.That(editor.Vm.CanUndo, Is.False);
        Assert.That(grid.RequestSamplePlacement(), Is.True);
        Assert.That(editor.Vm.SelectedSamples.Single(), Is.SameAs(editor.Node));
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
    [Test]
    public void ZoomDuringGestureKeepsSourcesHiddenAndRestoresHitTesting()
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grid = Descendants<BeatGridView>(editor.Page).First();
        grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0;
        editor.Page.BeginNodePointer(grid, new Point(100, editor.Vm.TrackHeight / 2), new Point());
        editor.Page.UpdateNodePointer(new Point(20, editor.Vm.TrackHeight / 2), grid, new Point(125, editor.Vm.TrackHeight / 2));
        for (var step = 0; step < 16; step++) editor.Vm.ZoomOutCommand.Execute(null);
        Assert.That(grid.GetDisplayedSampleBounds(editor.Node).IsEmpty, Is.True);
        Assert.That(grid.FindSampleAt(new Point(100, editor.Vm.TrackHeight / 2)), Is.Null);
        Assert.That(editor.Vm.IsNodeDragActive, Is.True);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1000));
        editor.Page.CancelNodePointer();
        var bounds = grid.GetDisplayedSampleBounds(editor.Node);
        Assert.That(bounds.IsEmpty, Is.False);
        Assert.That(grid.FindSampleAt(new Point(100, editor.Vm.TrackHeight / 2)), Is.SameAs(editor.Node));
        Assert.That(grid.FindSampleAt(new Point(bounds.Right, editor.Vm.TrackHeight / 2)), Is.Null);
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
    [TestCase(false)]
    [TestCase(true)]
    public void DragPreviewTracksSnappedDestinationWithoutEditingSamples(bool crossTrack)
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grids = Descendants<BeatGridView>(editor.Page).ToArray();
        foreach (var grid in grids) { grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0; }
        var target = grids[crossTrack ? 1 : 0];
        editor.Page.BeginNodePointer(grids[0], new Point(100, editor.Vm.TrackHeight / 2), new Point());
        editor.Page.UpdateNodePointer(new Point(1, 1), target, new Point(125, editor.Vm.TrackHeight / 2));
        Assert.That(editor.Vm.IsNodeDragActive, Is.False);
        Assert.That(grids[0].FindSampleAt(new Point(100, editor.Vm.TrackHeight / 2)), Is.SameAs(editor.Node));
        Assert.That(target.DragPreviewTimestamps, Is.Null);
        editor.Page.UpdateNodePointer(new Point(20, 100), target, new Point(125, editor.Vm.TrackHeight / 2));
        Assert.That(editor.Vm.NodePreviewTimestamps[editor.Node], Is.EqualTo(1250));
        Assert.That(target.DragPreviewTimestamps, Is.EqualTo(new[] { 1250 }));
        editor.Page.UpdateNodePointer(new Point(30, 100), target, new Point(150, editor.Vm.TrackHeight / 2));
        Assert.That(target.DragPreviewTimestamps, Is.EqualTo(new[] { 1500 }));
        editor.Page.UpdateNodePointer(new Point(20, 100), target, new Point(125, editor.Vm.TrackHeight / 2));
        Assert.That(target.DragPreviewTimestamps, Is.EqualTo(new[] { 1250 }));
        Assert.That(grids[0].GetDisplayedSampleBounds(editor.Node).IsEmpty, Is.True);
        Assert.That(grids[0].FindSampleAt(new Point(100, editor.Vm.TrackHeight / 2)), Is.Null);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1000)); Assert.That(editor.Vm.CanUndo, Is.False);
        Assert.That(editor.Vm.Tracks.Sum(track => track.Samples.Count), Is.EqualTo(1));
        var complete = editor.Page.CompleteNodePointerAsync(); UiTestPump.Run(complete);
        Assert.That(complete.GetAwaiter().GetResult(), Is.True);
        Assert.That(editor.Vm.IsNodeDragActive, Is.False);
        Assert.That(target.DragPreviewTimestamps, Is.Null);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1250));
        Assert.That(target.FindSampleAt(new Point(125, editor.Vm.TrackHeight / 2)), Is.SameAs(editor.Node));
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }

    [TestCase("leave")]
    [TestCase("switch")]
    [TestCase("recycle")]
    [TestCase("unload")]
    [TestCase("source-recycle")]
    [TestCase("source-unload")]
    [TestCase("escape")]
    [TestCase("overlay")]
    [TestCase("capture")]
    [TestCase("deactivate")]
    public void ChangingInvalidOrRecycledTargetClearsTemporaryNode(string action)
    {
        var editor = CreateEditor(3); using var view = editor.View;
        var grids = Descendants<BeatGridView>(editor.Page).ToArray();
        foreach (var grid in grids) { grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0; }
        var other = new Sample { Timestamp = 1500, FileName = "b.wav" }; editor.Vm.Tracks[1].Samples.Add(other);
        editor.Vm.SetNodeSelection([editor.Node, other]);
        editor.Page.BeginNodePointer(grids[0], new Point(100, editor.Vm.TrackHeight / 2), new Point());
        editor.Page.UpdateNodePointer(new Point(20, 100), grids[1], new Point(125, editor.Vm.TrackHeight / 2));
        Assert.That(editor.Vm.NodePreviewTimestamps[editor.Node], Is.EqualTo(1250));
        Assert.That(grids[1].DragPreviewTimestamps, Is.EqualTo(new[] { 1250 }));
        Assert.That(grids[0].GetDisplayedSampleBounds(editor.Node).IsEmpty, Is.True);
        switch (action)
        {
            case "leave": editor.Page.UpdateNodePointer(new Point(50, 500), null, default); break;
            case "switch": editor.Page.UpdateNodePointer(new Point(50, editor.Vm.TrackHeight / 2), grids[0], new Point(150, editor.Vm.TrackHeight / 2)); break;
            case "recycle": grids[1].DataContext = new Soundie.Core.Entities.Track(); break;
            case "unload": grids[1].RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, grids[1])); break;
            case "source-recycle": grids[0].DataContext = new Soundie.Core.Entities.Track(); break;
            case "source-unload": grids[0].RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, grids[0])); break;
            case "escape": editor.Page.HandlePlaybackKey(Key.Escape, ModifierKeys.None, false, grids[0]); break;
            case "overlay": editor.Page.ShowVolumeOverlay(false, new Point(), false, false); break;
            case "capture": editor.Page.TrackListComponent.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent }); break;
            case "deactivate": UiTestPump.Run(editor.Page.DeactivateAsync()); break;
        }
        if (action is "switch" or "leave") Assert.That(grids[0].GetDisplayedSampleBounds(editor.Node).IsEmpty, Is.True);
        Assert.That(grids[1].DragPreviewTimestamps, action == "switch" ? Is.EqualTo(new[] { 2000 }) : Is.Null);
        if (action == "switch")
        {
            Assert.That(editor.Vm.NodePreviewTimestamps[editor.Node], Is.EqualTo(1500));
            Assert.That(grids[0].DragPreviewTimestamps, Is.EqualTo(new[] { 1500 }));
        }
        editor.Page.CancelNodePointer();
        Assert.That(grids[0].DragPreviewTimestamps, Is.Null);
        Assert.That(grids[1].DragPreviewTimestamps, Is.Null);
        Assert.That(grids[0].GetDisplayedSampleBounds(editor.Node).IsEmpty, Is.False);
        if (action is not ("recycle" or "unload")) Assert.That(grids[1].GetDisplayedSampleBounds(other).IsEmpty, Is.False);
        Assert.That(other.Timestamp, Is.EqualTo(1500));
        var list = (ListBox)((Soundie.Views.Pages.OHsWorkstationTrackList)editor.Page.FindName("TrackListComponent")).FindName("TrackList");
        Assert.That(AdornerLayer.GetAdornerLayer(list)!.GetAdorners(list), Is.Null);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1000)); Assert.That(editor.Vm.CanUndo, Is.False);
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SelectedGroupDragCommitsAllNodesTogether(bool crossTrack)
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grids = Descendants<BeatGridView>(editor.Page).ToArray();
        foreach (var item in grids) { item.ContentWidth = 1000; item.ViewportWidth = 500; item.HorizontalOffset = 0; }
        var grid = grids[0];
        var target = grids[crossTrack ? 1 : 0];
        var other = new Sample { Timestamp = 1500, FileName = "a.wav" }; editor.Vm.Tracks[0].Samples.Add(other);
        editor.Vm.SetNodeSelection([editor.Node, other]);
        editor.Page.BeginNodePointer(grid, new Point(100, editor.Vm.TrackHeight / 2), new Point());
        editor.Page.UpdateNodePointer(new Point(20, editor.Vm.TrackHeight / 2), target, new Point(125, editor.Vm.TrackHeight / 2));
        Assert.That(grid.GetDisplayedSampleBounds(editor.Node).IsEmpty, Is.True);
        Assert.That(grid.GetDisplayedSampleBounds(other).IsEmpty, Is.True);
        Assert.That(other.Timestamp, Is.EqualTo(1500)); Assert.That(editor.Vm.CanUndo, Is.False);
        UiTestPump.Run(editor.Page.CompleteNodePointerAsync());
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1250)); Assert.That(other.Timestamp, Is.EqualTo(1750));
        Assert.That(editor.Vm.Tracks[crossTrack ? 1 : 0].Samples, Is.EquivalentTo(new[] { editor.Node, other }));
        Assert.That(target.GetDisplayedSampleBounds(other).IsEmpty, Is.False);
        editor.Vm.UndoCommand.Execute(null);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1000)); Assert.That(other.Timestamp, Is.EqualTo(1500));
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
    [Test]
    public void ControlClickTogglesAcrossTracksWithoutRearmingRemovedNode()
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grids = Descendants<BeatGridView>(editor.Page).ToArray();
        foreach (var grid in grids) { grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0; }
        var other = new Sample { Timestamp = 1500, FileName = "b.wav" }; editor.Vm.Tracks[1].Samples.Add(other);
        editor.Page.BeginNodePointer(grids[0], new Point(100, editor.Vm.TrackHeight / 2), new Point());
        editor.Page.BeginNodePointer(grids[1], new Point(150, editor.Vm.TrackHeight / 2), new Point(), ModifierKeys.Control);
        UiTestPump.Run(editor.Page.CompleteNodePointerAsync());
        Assert.That(editor.Vm.SelectedSamples, Is.EquivalentTo(new[] { editor.Node, other }));
        Assert.That(editor.Page.BeginNodePointer(grids[1], new Point(150, editor.Vm.TrackHeight / 2), new Point(), ModifierKeys.Control), Is.True);
        editor.Page.UpdateNodePointer(new Point(20, 20), grids[1], new Point(200, editor.Vm.TrackHeight / 2));
        var completion = editor.Page.CompleteNodePointerAsync(); UiTestPump.Run(completion);
        Assert.That(completion.GetAwaiter().GetResult(), Is.False);
        Assert.That(editor.Vm.SelectedSamples, Is.EquivalentTo(new[] { editor.Node, other }));
        Assert.That(other.Timestamp, Is.EqualTo(1500)); Assert.That(editor.Vm.CanUndo, Is.False);
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
    [Test]
    public void PlainClickPreservesSelectedDragGroup()
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grid = Descendants<BeatGridView>(editor.Page).First();
        grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0;
        var other = new Sample { Timestamp = 1500, FileName = "a.wav" }; editor.Vm.Tracks[0].Samples.Add(other);
        editor.Vm.SetNodeSelection([editor.Node, other]); editor.Vm.ScrollWithMarker = true;
        editor.Page.BeginNodePointer(grid, new Point(100, editor.Vm.TrackHeight / 2), new Point());
        Assert.That(editor.Vm.SelectedSamples, Is.EquivalentTo(new[] { editor.Node, other }));
        Assert.That(editor.Vm.ScrollWithMarker, Is.False);
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
    [TestCase(ModifierKeys.None)]
    [TestCase(ModifierKeys.Control)]
    public void OutsideClickClearsSelectionWithoutHandlingOtherInput(ModifierKeys modifiers)
    {
        var editor = CreateEditor(); using var view = editor.View;
        editor.Vm.SelectNode(editor.Vm.Tracks[0], editor.Node, false);
        Assert.That(editor.Page.BeginNodePointer(null, new Point(), new Point(), modifiers), Is.False);
        Assert.That(editor.Vm.SelectedSamples, Is.Empty);
        var name = Descendants<TextBox>(editor.Page).First();
        editor.Vm.SelectNode(editor.Vm.Tracks[0], editor.Node, false);
        var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
        name.RaiseEvent(click);
        Assert.That(editor.Vm.SelectedSamples, Is.Empty); Assert.That(click.Handled, Is.False);
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
    [Test]
    public void KeyboardToggleAndEscapeRespectTextEditing()
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grid = Descendants<BeatGridView>(editor.Page).First(); grid.SetActivePosition(TimeSpan.FromMilliseconds(1000));
        Assert.That(editor.Page.HandlePlaybackKey(Key.Enter, ModifierKeys.Control, false, grid), Is.True);
        Assert.That(editor.Vm.SelectedSamples.Single(), Is.SameAs(editor.Node));
        Assert.That(editor.Page.HandlePlaybackKey(Key.Enter, ModifierKeys.Control, true, grid), Is.True);
        Assert.That(editor.Vm.SelectedSamples, Has.Count.EqualTo(1));
        Assert.That(editor.Page.HandlePlaybackKey(Key.Escape, ModifierKeys.None, false, new TextBox()), Is.False);
        Assert.That(editor.Page.HandlePlaybackKey(Key.Escape, ModifierKeys.None, false, grid), Is.True);
        Assert.That(editor.Vm.SelectedSamples, Is.Empty);
        grid.SetActivePosition(TimeSpan.FromMilliseconds(2000));
        Assert.That(editor.Page.HandlePlaybackKey(Key.Enter, ModifierKeys.Control, false, grid), Is.True);
        Assert.That(editor.Vm.Tracks[0].Samples, Has.Count.EqualTo(1));
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
    [SetUp] public void Setup() => new VolumeWheelTests().SetUp();
    [Test]
    public void TrackCardsExposeNativeSingleSelection()
    {
        var page = WorkstationTestPage.Create();
        Assert.That(((Soundie.Views.Pages.OHsWorkstationTrackList)page.FindName("TrackListComponent")).FindName("TrackList"), Is.TypeOf<ListBox>());
        var list = (ListBox)((Soundie.Views.Pages.OHsWorkstationTrackList)page.FindName("TrackListComponent")).FindName("TrackList");
        Assert.That(list.SelectionMode, Is.EqualTo(SelectionMode.Single));
        Assert.That(list.Style, Is.SameAs(page.FindResource("BaseListBoxStyle")));
    }

    private static (WorkstationPage Page, WorkstationPageVM Vm, Sample Node, InMemoryView View) CreateEditor(int trackCount = 2, int bpm = 120, int offset = 0)
    {
        var node = new Sample { Timestamp = 1000, FileName = "a.wav" };
        var data = new ProjectData { Offsets = [offset], BPMs = [bpm], Tracks = [new Track { SelectedSampleFileName = "a.wav", Samples = [node] }, new Track { SelectedSampleFileName = "b.wav" }] };
        data.Tracks.AddRange(Enumerable.Range(2, trackCount - 2).Select(_ => new Track { SelectedSampleFileName = "b.wav" }));
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", data)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["a.wav", "b.wav"]), nodePasteDialog: new StubNodePasteDialogService());
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        var host = new Window { Content = page, Width = 1100, Height = 800 };
        var view = new InMemoryView(host); UiTestPump.Run(page.LoadCompletion); view.UpdateLayout();
        // Realize the explicit participants; leave larger collections virtualized.
        host.Height += Math.Min(trackCount, 6) * vm.TrackHeight; view.UpdateLayout();
        return (page, vm, node, view);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    [Test]
    public void DeleteNeedsSelectionAndTextKeepsEditingShortcuts()
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grid = Descendants<BeatGridView>(editor.Page).First(); grid.SetActivePosition(TimeSpan.FromMilliseconds(1000));
        Assert.That(editor.Page.HandlePlaybackKey(Key.Delete, ModifierKeys.None, false, grid), Is.False);
        Assert.That(editor.Vm.Tracks[0].Samples.Single(), Is.SameAs(editor.Node));
        editor.Vm.SelectNode(editor.Vm.Tracks[0], editor.Node, false);
        Assert.That(editor.Page.HandlePlaybackKey(Key.Delete, ModifierKeys.None, false, new TextBox()), Is.False);
        Assert.That(editor.Page.HandlePlaybackKey(Key.C, ModifierKeys.Control, false, new TextBox()), Is.False);
        Assert.That(editor.Page.HandlePlaybackKey(Key.Delete, ModifierKeys.None, false, grid), Is.True);
        Assert.That(editor.Vm.Tracks[0].Samples, Is.Empty);
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
    [TestCase(false)]
    [TestCase(true)]
    public void CursorAdornerIsRemovedOnDropOrOverlayCancellation(bool cancel)
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grids = Descendants<BeatGridView>(editor.Page).ToArray(); Assert.That(grids.Length, Is.EqualTo(2));
        foreach (var grid in grids) { grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0; }
        Assert.That(editor.Page.BeginNodePointer(grids[0], new Point(100, editor.Vm.TrackHeight / 2), new Point(0, 0)), Is.True);
        var list = (ListBox)((Soundie.Views.Pages.OHsWorkstationTrackList)editor.Page.FindName("TrackListComponent")).FindName("TrackList"); var layer = AdornerLayer.GetAdornerLayer(list)!;
        editor.Page.UpdateNodePointer(new Point(1, 1), grids[1], new Point(125, editor.Vm.TrackHeight / 2));
        Assert.That(layer.GetAdorners(list), Is.Null);
        editor.Page.UpdateNodePointer(new Point(20, 100), grids[1], new Point(125, editor.Vm.TrackHeight / 2));
        Assert.That(layer.GetAdorners(list)!.Single(), Is.TypeOf<OHsSampleNodeDragAdorner>());
        Assert.That(layer.GetAdorners(list)!.Single().IsHitTestVisible, Is.False);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1000));
        if (cancel) editor.Page.ShowVolumeOverlay(true, new Point(), false, false);
        else UiTestPump.Run(editor.Page.CompleteNodePointerAsync());
        Assert.That(layer.GetAdorners(list), Is.Null);
        Assert.That(editor.Vm.IsNodeDragActive, Is.False);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(cancel ? 1000 : 1250));
        Assert.That(editor.Vm.Tracks[cancel ? 0 : 1].Samples.Contains(editor.Node), Is.True);
        Assert.That(grids[cancel ? 0 : 1].GetDisplayedSampleBounds(editor.Node).IsEmpty, Is.False);
        Assert.That(grids[0].DragPreviewTimestamps, Is.Null);
        Assert.That(grids[1].DragPreviewTimestamps, Is.Null);
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidCursorCancelsDragWithoutEditing(double invalid)
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grid = Descendants<BeatGridView>(editor.Page).First();
        grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0;
        Assert.That(editor.Page.BeginNodePointer(grid, new Point(100, editor.Vm.TrackHeight / 2), new Point(0, 0)), Is.True);
        editor.Page.UpdateNodePointer(new Point(invalid, editor.Vm.TrackHeight / 2), grid, new Point(100, editor.Vm.TrackHeight / 2));
        Assert.That(editor.Vm.IsNodeDragActive, Is.False);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1000));
        Assert.That(editor.Vm.CanUndo, Is.False);
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }

    [Test]
    public void CollisionRestoresSourcesAndRemovesCursorGhost()
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grids = Descendants<BeatGridView>(editor.Page).ToArray();
        foreach (var grid in grids) { grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0; }
        var occupied = new Sample { Timestamp = 1250, FileName = "b.wav" };
        editor.Vm.Tracks[1].Samples.Add(occupied);
        editor.Page.BeginNodePointer(grids[0], new Point(100, editor.Vm.TrackHeight / 2), new Point());
        editor.Page.UpdateNodePointer(new Point(20, 100), grids[1], new Point(125, editor.Vm.TrackHeight / 2));
        Assert.That(grids[0].GetDisplayedSampleBounds(editor.Node).IsEmpty, Is.True);
        var complete = editor.Page.CompleteNodePointerAsync(); UiTestPump.Run(complete);
        Assert.That(complete.Result, Is.False);
        Assert.That(grids[0].DragPreviewTimestamps, Is.Null);
        Assert.That(grids[1].DragPreviewTimestamps, Is.Null);
        Assert.That(grids[0].FindSampleAt(new Point(100, editor.Vm.TrackHeight / 2)), Is.SameAs(editor.Node));
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1000));
        Assert.That(editor.Vm.Tracks[1].Samples.Single(), Is.SameAs(occupied));
        var list = (ListBox)((Soundie.Views.Pages.OHsWorkstationTrackList)editor.Page.FindName("TrackListComponent")).FindName("TrackList");
        Assert.That(AdornerLayer.GetAdornerLayer(list)!.GetAdorners(list), Is.Null);
        Assert.That(editor.Vm.CanUndo, Is.False);
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }

    [Test]
    public void MissingAdornerLayerCancelsDragAndKeepsSourceVisible()
    {
        var editor = CreateEditor(); using var view = editor.View;
        var detached = new WorkstationPage(editor.Vm);
        UiTestPump.Layout(detached, 900, 600);
        Descendants<AdornerDecorator>(detached).Single().Child = null;
        var grid = new BeatGridView { DataContext = editor.Vm.Tracks[0], Samples = editor.Vm.Tracks[0].Samples,
            CanPlaceSamples = true, Duration = TimeSpan.FromSeconds(10), ContentWidth = 1000, ViewportWidth = 500 };
        UiTestPump.Layout(grid, 500, 36);
        Assert.That(AdornerLayer.GetAdornerLayer((ListBox)((Soundie.Views.Pages.OHsWorkstationTrackList)detached.FindName("TrackListComponent")).FindName("TrackList")), Is.Null);
        Assert.That(detached.BeginNodePointer(grid, new Point(100, 18), new Point()), Is.True);
        detached.UpdateNodePointer(new Point(20, 20), grid, new Point(125, 18));
        Assert.That(editor.Vm.IsNodeDragActive, Is.False);
        Assert.That(grid.FindSampleAt(new Point(100, 18)), Is.SameAs(editor.Node));
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1000));
        UiTestPump.Run(detached.DeactivateAsync());
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
    [TestCase(Key.Delete, ModifierKeys.None)]
    [TestCase(Key.Right, ModifierKeys.Alt)]
    public void EditingHotkeyEndsPointerGesture(Key key, ModifierKeys modifiers)
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grid = Descendants<BeatGridView>(editor.Page).First();
        grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0;
        editor.Page.BeginNodePointer(grid, new Point(100, editor.Vm.TrackHeight / 2), new Point(0, 0));
        editor.Page.UpdateNodePointer(new Point(20, 20), grid, new Point(125, editor.Vm.TrackHeight / 2));
        var list = (ListBox)((Soundie.Views.Pages.OHsWorkstationTrackList)editor.Page.FindName("TrackListComponent")).FindName("TrackList"); var layer = AdornerLayer.GetAdornerLayer(list)!;
        Assert.That(editor.Page.HandlePlaybackKey(key, modifiers, false, grid), Is.True);
        UiTestPump.Run(editor.Page.NodeInputCompletion);
        Assert.That(layer.GetAdorners(list), Is.Null);
        Assert.That(editor.Vm.IsNodeDragActive, Is.False);
        if (key == Key.Delete) Assert.That(editor.Vm.Tracks[0].Samples, Is.Empty);
        else Assert.That(editor.Node.Timestamp, Is.EqualTo(1125));
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
    [Test]
    public void UnloadingSourceGridPreservesModelGestureUntilCancelled()
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grid = Descendants<BeatGridView>(editor.Page).First();
        grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0;
        editor.Page.BeginNodePointer(grid, new Point(100, editor.Vm.TrackHeight / 2), new Point(0, 0));
        editor.Page.UpdateNodePointer(new Point(20, 20), grid, new Point(125, editor.Vm.TrackHeight / 2));
        grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, grid));
        Assert.That(editor.Vm.IsNodeDragActive, Is.True);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1000));
        editor.Page.CancelNodePointer();
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
}
