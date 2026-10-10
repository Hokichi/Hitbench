using System.Windows.Controls;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationNodeInteractionTests
{
    [Test]
    public void BlankClickInOccupiedIntervalDoesNotReselectOnRelease()
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grid = Descendants<BeatGridView>(editor.Page).First();
        grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0;
        editor.Vm.SelectNode(editor.Vm.Tracks[0], editor.Node, false);
        Assert.That(editor.Page.BeginNodePointer(grid, new Point(111, 10), new Point()), Is.False);
        Assert.That(editor.Vm.SelectedSamples, Is.Empty);
        grid.UpdatePointerPosition(111);
        Assert.That(grid.RequestPointerSamplePlacement(), Is.True);
        Assert.That(editor.Vm.SelectedSamples, Is.Empty);
        Assert.That(editor.Vm.Tracks[0].Samples, Has.Count.EqualTo(1));
        Assert.That(editor.Vm.CanUndo, Is.False);
        Assert.That(grid.RequestSamplePlacement(), Is.True);
        Assert.That(editor.Vm.SelectedSamples.Single(), Is.SameAs(editor.Node));
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
    [Test]
    public void ZoomDuringGestureUpdatesBoundHitGeometryWithoutChangingSamples()
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grid = Descendants<BeatGridView>(editor.Page).First();
        grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0;
        editor.Page.BeginNodePointer(grid, new Point(100, 10), new Point());
        editor.Page.UpdateNodePointer(new Point(20, 10), grid, new Point(125, 10));
        for (var step = 0; step < 16; step++) editor.Vm.ZoomOutCommand.Execute(null);
        Assert.That(grid.FindSampleAt(new Point(129, 10)), Is.SameAs(editor.Node));
        Assert.That(grid.FindSampleAt(new Point(130, 10)), Is.Null);
        Assert.That(editor.Vm.IsNodeDragActive, Is.True);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1000));
        editor.Page.CancelNodePointer();
        Assert.That(grid.DragPreviewTimestamp, Is.Null);
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
        editor.Page.BeginNodePointer(grids[0], new Point(100, 10), new Point());
        editor.Page.UpdateNodePointer(new Point(1, 1), target, new Point(125, 10));
        Assert.That(target.DragPreviewTimestamp, Is.Null);
        editor.Page.UpdateNodePointer(new Point(20, 100), target, new Point(125, 10));
        Assert.That(target.DragPreviewTimestamp, Is.EqualTo(1250));
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1000)); Assert.That(editor.Vm.CanUndo, Is.False);
        Assert.That(editor.Vm.Tracks.Sum(track => track.Samples.Count), Is.EqualTo(1));
        var complete = editor.Page.CompleteNodePointerAsync(); UiTestPump.Run(complete);
        Assert.That(complete.GetAwaiter().GetResult(), Is.True);
        Assert.That(target.DragPreviewTimestamp, Is.Null);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1250));
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }

    [TestCase("leave")]
    [TestCase("switch")]
    [TestCase("recycle")]
    [TestCase("unload")]
    [TestCase("escape")]
    [TestCase("overlay")]
    [TestCase("capture")]
    [TestCase("deactivate")]
    public void ChangingInvalidOrRecycledTargetClearsTemporaryNode(string action)
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grids = Descendants<BeatGridView>(editor.Page).ToArray();
        foreach (var grid in grids) { grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0; }
        editor.Page.BeginNodePointer(grids[0], new Point(100, 10), new Point());
        editor.Page.UpdateNodePointer(new Point(20, 100), grids[1], new Point(125, 10));
        Assert.That(grids[1].DragPreviewTimestamp, Is.EqualTo(1250));
        switch (action)
        {
            case "leave": editor.Page.UpdateNodePointer(new Point(50, 500), null, default); break;
            case "switch": editor.Page.UpdateNodePointer(new Point(50, 10), grids[0], new Point(150, 10)); break;
            case "recycle": grids[1].DataContext = new osuHitsounder.Core.Entities.Track(); break;
            case "unload": grids[1].RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, grids[1])); break;
            case "escape": editor.Page.HandlePlaybackKey(Key.Escape, ModifierKeys.None, false, grids[0]); break;
            case "overlay": editor.Page.ShowVolumeOverlay(false, new Point(), false, false); break;
            case "capture": editor.Page.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent }); break;
            case "deactivate": UiTestPump.Run(editor.Page.DeactivateAsync()); break;
        }
        Assert.That(grids[1].DragPreviewTimestamp, Is.Null);
        if (action == "switch") Assert.That(grids[0].DragPreviewTimestamp, Is.EqualTo(1500));
        editor.Page.CancelNodePointer();
        Assert.That(grids[0].DragPreviewTimestamp, Is.Null);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1000)); Assert.That(editor.Vm.CanUndo, Is.False);
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }

    [Test]
    public void SelectedGroupDragCommitsAllNodesTogether()
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grid = Descendants<BeatGridView>(editor.Page).First(); grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0;
        var other = new Sample { Timestamp = 1500, FileName = "a.wav" }; editor.Vm.Tracks[0].Samples.Add(other);
        editor.Vm.SetNodeSelection([editor.Node, other]);
        editor.Page.BeginNodePointer(grid, new Point(100, 10), new Point());
        editor.Page.UpdateNodePointer(new Point(20, 10), grid, new Point(125, 10));
        Assert.That(other.Timestamp, Is.EqualTo(1500)); Assert.That(editor.Vm.CanUndo, Is.False);
        UiTestPump.Run(editor.Page.CompleteNodePointerAsync());
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1250)); Assert.That(other.Timestamp, Is.EqualTo(1750));
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
        editor.Page.BeginNodePointer(grids[0], new Point(100, 10), new Point());
        editor.Page.BeginNodePointer(grids[1], new Point(150, 10), new Point(), ModifierKeys.Control);
        Assert.That(editor.Vm.SelectedSamples, Is.EquivalentTo(new[] { editor.Node, other }));
        Assert.That(editor.Page.BeginNodePointer(grids[1], new Point(150, 10), new Point(), ModifierKeys.Control), Is.True);
        editor.Page.UpdateNodePointer(new Point(20, 20), grids[1], new Point(200, 10));
        var completion = editor.Page.CompleteNodePointerAsync(); UiTestPump.Run(completion);
        Assert.That(completion.GetAwaiter().GetResult(), Is.False);
        Assert.That(editor.Vm.SelectedSamples.Single(), Is.SameAs(editor.Node));
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
        editor.Page.BeginNodePointer(grid, new Point(100, 10), new Point());
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
        Assert.That(page.FindName("TrackList"), Is.TypeOf<ListBox>());
        var list = (ListBox)page.FindName("TrackList");
        Assert.That(list.SelectionMode, Is.EqualTo(SelectionMode.Single));
        Assert.That(list.Style, Is.SameAs(page.FindResource("OHsWorkstationTrackListStyle")));
    }

    private static (WorkstationPage Page, WorkstationPageVM Vm, Sample Node, InMemoryView View) CreateEditor()
    {
        var node = new Sample { Timestamp = 1000, FileName = "a.wav" };
        var data = new ProjectData { Offsets = [0], BPMs = [120], Tracks = [new Track { SelectedSampleFileName = "a.wav", Samples = [node] }, new Track { SelectedSampleFileName = "b.wav" }] };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", data)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["a.wav", "b.wav"]), nodePasteDialog: new StubNodePasteDialogService());
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        var view = new InMemoryView(new Window { Content = page, Width = 1100, Height = 800 }); UiTestPump.Run(page.LoadCompletion); view.UpdateLayout();
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
        Assert.That(editor.Page.BeginNodePointer(grids[0], new Point(100, 10), new Point(0, 0)), Is.True);
        var list = (ListBox)editor.Page.FindName("TrackList"); var layer = AdornerLayer.GetAdornerLayer(list)!;
        editor.Page.UpdateNodePointer(new Point(1, 1), grids[1], new Point(125, 10));
        Assert.That(layer.GetAdorners(list), Is.Null);
        editor.Page.UpdateNodePointer(new Point(20, 100), grids[1], new Point(125, 10));
        Assert.That(layer.GetAdorners(list)!.Single(), Is.TypeOf<OHsSampleNodeDragAdorner>());
        Assert.That(layer.GetAdorners(list)!.Single().IsHitTestVisible, Is.False);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1000));
        if (cancel) editor.Page.ShowVolumeOverlay(true, new Point(), false, false);
        else UiTestPump.Run(editor.Page.CompleteNodePointerAsync());
        Assert.That(layer.GetAdorners(list), Is.Null);
        Assert.That(editor.Vm.IsNodeDragActive, Is.False);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(cancel ? 1000 : 1250));
        Assert.That(editor.Vm.Tracks[cancel ? 0 : 1].Samples.Contains(editor.Node), Is.True);
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidCursorCancelsDragWithoutEditing(double invalid)
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grid = Descendants<BeatGridView>(editor.Page).First();
        grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0;
        Assert.That(editor.Page.BeginNodePointer(grid, new Point(100, 10), new Point(0, 0)), Is.True);
        editor.Page.UpdateNodePointer(new Point(invalid, 10), grid, new Point(100, 10));
        Assert.That(editor.Vm.IsNodeDragActive, Is.False);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1000));
        Assert.That(editor.Vm.CanUndo, Is.False);
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
    [TestCase(Key.Delete, ModifierKeys.None)]
    [TestCase(Key.Right, ModifierKeys.Alt)]
    public void EditingHotkeyEndsPointerGesture(Key key, ModifierKeys modifiers)
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grid = Descendants<BeatGridView>(editor.Page).First();
        grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0;
        editor.Page.BeginNodePointer(grid, new Point(100, 10), new Point(0, 0));
        editor.Page.UpdateNodePointer(new Point(20, 20), grid, new Point(125, 10));
        var list = (ListBox)editor.Page.FindName("TrackList"); var layer = AdornerLayer.GetAdornerLayer(list)!;
        Assert.That(editor.Page.HandlePlaybackKey(key, modifiers, false, grid), Is.True);
        UiTestPump.Run(editor.Page.NodeInputCompletion);
        Assert.That(layer.GetAdorners(list), Is.Null);
        Assert.That(editor.Vm.IsNodeDragActive, Is.False);
        if (key == Key.Delete) Assert.That(editor.Vm.Tracks[0].Samples, Is.Empty);
        else Assert.That(editor.Node.Timestamp, Is.EqualTo(1125));
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
    [Test]
    public void UnloadingSourceGridCancelsGesture()
    {
        var editor = CreateEditor(); using var view = editor.View;
        var grid = Descendants<BeatGridView>(editor.Page).First();
        grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0;
        editor.Page.BeginNodePointer(grid, new Point(100, 10), new Point(0, 0));
        editor.Page.UpdateNodePointer(new Point(20, 20), grid, new Point(125, 10));
        grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, grid));
        Assert.That(editor.Vm.IsNodeDragActive, Is.False);
        Assert.That(editor.Node.Timestamp, Is.EqualTo(1000));
        UiTestPump.Run(editor.Page.DeactivateAsync());
    }
}
