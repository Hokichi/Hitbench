using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;
using Track = osuHitsounder.Core.Entities.Track;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationNodePlaceholderTests
{
    private WorkstationPage page = null!;
    private WorkstationPageVM vm = null!;
    private InMemoryView view = null!;
    private Window host = null!;
    private Sample a = null!, b = null!;

    [SetUp] public void Setup() => new VolumeWheelTests().SetUp();
    [TearDown]
    public void Cleanup() { if (page is not null) UiTestPump.Run(page.DeactivateAsync()); view?.Dispose(); }

    private void CreateEditor(int trackCount = 6)
    {
        a = new Sample { Timestamp = 1000, FileName = "a.wav" };
        b = new Sample { Timestamp = 1500, FileName = "b.wav" };
        var tracks = Enumerable.Range(0, trackCount).Select(_ => new Track { SelectedSampleFileName = "b.wav" }).ToList();
        tracks[0].SelectedSampleFileName = "a.wav"; tracks[0].Samples.Add(a); tracks[3].Samples.Add(b);
        var data = new ProjectData { Offsets = [0], BPMs = [120], Tracks = tracks };
        vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", data)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            new StubSampleFileService(["a.wav", "b.wav"]), nodePasteDialog: new StubNodePasteDialogService());
        page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        host = new Window { Content = page, Width = 1100, Height = 800 };
        view = new InMemoryView(host); UiTestPump.Run(page.LoadCompletion);
        vm.ToggleTracksCollapsedCommand.Execute(null); view.UpdateLayout();
        host.Height += Math.Min(trackCount, 6) * vm.TrackHeight; view.UpdateLayout();
        foreach (var grid in Grids()) { grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0; }
    }

    [Test]
    public void EverySelectedNodeHasDestinationPlaceholder()
    {
        CreateEditor();
        vm.Tracks[3].Samples.Remove(b); vm.Tracks[0].Samples.Add(b);
        vm.SetNodeSelection([a, b]);
        var grid = Grid(vm.Tracks[0]);
        page.BeginNodePointer(grid, new Point(100, grid.ActualHeight / 2), new Point());
        page.UpdateNodePointer(new Point(200, 200), grid, new Point(125, grid.ActualHeight / 2));
        Assert.That(NodeShapes(grid, [1250, 1750]), Has.Length.EqualTo(2));
        Assert.That(new[] { a.Timestamp, b.Timestamp }, Is.EqualTo(new[] { 1000, 1500 }));
        Assert.That(vm.CanUndo, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PreviewDistributesAllNodesAcrossMappedTracks(bool sameTime)
    {
        CreateEditor();
        if (sameTime) b.Timestamp = a.Timestamp;
        var c = new Sample { Timestamp = 1250, FileName = "a.wav" }; vm.Tracks[0].Samples.Add(c);
        vm.SetNodeSelection([a, c, b]); BeginLaterTrackDrag(); MoveTo(vm.Tracks[5]);
        Assert.That(Grid(vm.Tracks[2]).DragPreviewTimestamps, Is.EqualTo(new[] { 1250, 1500 }));
        Assert.That(Grid(vm.Tracks[5]).DragPreviewTimestamps, Is.EqualTo(new[] { sameTime ? 1250 : 1750 }));
        Assert.That(NodeShapes(Grid(vm.Tracks[2]), [1250, 1500]), Has.Length.EqualTo(2));
        Assert.That(NodeShapes(Grid(vm.Tracks[5]), [sameTime ? 1250 : 1750]), Has.Length.EqualTo(1));
        Assert.That(vm.NodePreviewByTrack.Values.Sum(nodes => nodes.Count), Is.EqualTo(3));
        Assert.That(vm.CanUndo, Is.False);
    }

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void PlaceholdersUseActualNodeGeometry(FlowDirection direction)
    {
        var timestamps = new[] { 1250, 1750 };
        var grid = new BeatGridView { Duration = TimeSpan.FromSeconds(10), ContentWidth = 1000, ViewportWidth = 400,
            CanPlaceSamples = true, AllowNodeCreation = false, HoverBrush = Brushes.Green, MarkerBrush = Brushes.Blue,
            FlowDirection = direction };
        for (var phase = 0; phase < 2; phase++)
        {
            grid.ContentWidth = phase == 0 ? 1000 : 2000; grid.HorizontalOffset = phase == 0 ? 32 : 100;
            grid.ZoomFactor = phase == 0 ? 32 : 8;
            UiTestPump.Layout(grid, 400, phase == 0 ? 72 : 120);
            grid.DragPreviewTimestamps = null; grid.Samples = timestamps.Select(t => new Sample { Timestamp = t }).ToArray();
            var actual = NodeShapes(grid, timestamps);
            grid.Samples = []; grid.DragPreviewTimestamps = timestamps;
            Assert.That(actual, Has.Length.EqualTo(2));
            Assert.That(NodeShapes(grid, timestamps), Is.EquivalentTo(actual));
            foreach (var t in timestamps) Assert.That(grid.FindSampleAt(Center(grid.GetSampleBounds(t))), Is.Null);
            Assert.That(grid.GetSamplesIntersecting(new Rect(0, 0, 400, grid.ActualHeight)), Is.Empty);
            grid.SetActivePosition(TimeSpan.FromMilliseconds(1250));
            Assert.That(grid.FindActiveSample(), Is.Null); Assert.That(grid.RequestActiveSampleRemoval(), Is.False);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TargetSwitchAndInvalidRecoveryReplaceWholeGroup(bool recover)
    {
        CreateEditor(); vm.SetNodeSelection([a, b]); BeginLaterTrackDrag(); MoveTo(vm.Tracks[5]);
        var oldA = Grid(vm.Tracks[2]); var oldB = Grid(vm.Tracks[5]);
        MoveTo(vm.Tracks[4]);
        Assert.That(oldA.DragPreviewTimestamps, Is.Null); Assert.That(oldB.DragPreviewTimestamps, Is.Null);
        Assert.That(Grid(vm.Tracks[1]).DragPreviewTimestamps, Is.EqualTo(new[] { 1250 }));
        Assert.That(Grid(vm.Tracks[4]).DragPreviewTimestamps, Is.EqualTo(new[] { 1750 }));
        page.UpdateNodePointer(new Point(300, 300), null, default);
        AssertEmptyPlaceholders(); Assert.That(vm.IsNodeDragActive, Is.True);
        MoveTo(vm.Tracks[5]); MoveTo(vm.Tracks[0]);
        AssertEmptyPlaceholders(); Assert.That(vm.IsNodeDragActive, Is.True);
        if (recover) MoveTo(vm.Tracks[5]);
        var completion = page.CompleteNodePointerAsync(); UiTestPump.Run(completion);
        Assert.That(completion.Result, Is.EqualTo(recover)); AssertEmptyPlaceholders();
        Assert.That(vm.Tracks[recover ? 2 : 0].Samples.Contains(a), Is.True);
        Assert.That(vm.Tracks[recover ? 5 : 3].Samples.Contains(b), Is.True);
        Assert.That(new[] { a.Timestamp, b.Timestamp }, Is.EqualTo(recover ? new[] { 1250, 1750 } : new[] { 1000, 1500 }));
    }

    [Test]
    public void NewlyRealizedDestinationGetsCurrentPreview()
    {
        CreateEditor(40); vm.Tracks[3].Samples.Remove(b); vm.Tracks[30].Samples.Add(b);
        vm.SetNodeSelection([a, b]);
        var list = (ListBox)((osuHitsounder.Views.Pages.OHsWorkstationTrackList)page.FindName("TrackListComponent")).FindName("TrackList");
        Assert.That(list.ItemContainerGenerator.ContainerFromIndex(30), Is.Null);
        var source = Grid(vm.Tracks[0]);
        page.BeginNodePointer(source, new Point(100, source.ActualHeight / 2), new Point());
        page.UpdateNodePointer(new Point(200, 200), source, new Point(125, source.ActualHeight / 2));
        Assert.That(vm.NodePreviewByTrack[vm.Tracks[30]], Is.EqualTo(new[] { 1750 }));
        Assert.That(list.ItemContainerGenerator.ContainerFromIndex(30), Is.Null);
        var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        host.Height += (item.ActualHeight + item.Margin.Top + item.Margin.Bottom) * 40;
        // Realize and load the new rows, then process their pending drawing layout.
        view.UpdateLayout();
        view.UpdateLayout();
        Assert.That(vm.IsNodeDragActive, Is.True);
        Assert.That(Grid(vm.Tracks[30]).DragPreviewTimestamps, Is.EqualTo(new[] { 1750 }));
    }

    [Test]
    public void RecycledNonAnchorDestinationDropsPreviousTrackPreview()
    {
        CreateEditor(); vm.SetNodeSelection([a, b]); BeginLaterTrackDrag(); MoveTo(vm.Tracks[5]);
        var destination = Grid(vm.Tracks[2]);
        Assert.That(destination.DragPreviewTimestamps, Is.EqualTo(new[] { 1250 }));
        destination.DataContext = vm.Tracks[1]; view.UpdateLayout();
        Assert.That(destination.DragPreviewTimestamps, Is.Null); Assert.That(vm.IsNodeDragActive, Is.True);
        destination.DataContext = vm.Tracks[2]; view.UpdateLayout();
        Assert.That(destination.DragPreviewTimestamps, Is.EqualTo(new[] { 1250 }));
    }

    [TestCase("drop")]
    [TestCase("escape")]
    [TestCase("capture")]
    [TestCase("source-recycle")]
    [TestCase("target-recycle")]
    [TestCase("source-unload")]
    [TestCase("target-unload")]
    [TestCase("overlay")]
    [TestCase("reload")]
    [TestCase("deactivate")]
    public void PlaceholderGroupClearsOnGestureEnd(string action)
    {
        CreateEditor(); vm.SetNodeSelection([a, b]); BeginLaterTrackDrag(); MoveTo(vm.Tracks[5]);
        var source = Grid(vm.Tracks[3]); var target = Grid(vm.Tracks[5]);
        Assert.That(Grid(vm.Tracks[2]).DragPreviewTimestamps, Is.EqualTo(new[] { 1250 }));
        switch (action)
        {
            case "drop": UiTestPump.Run(page.CompleteNodePointerAsync()); break;
            case "escape": page.HandlePlaybackKey(Key.Escape, ModifierKeys.None, false, source); break;
            case "capture": page.TrackListComponent.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent }); break;
            case "source-recycle": source.DataContext = new Track(); break;
            case "target-recycle": target.DataContext = new Track(); break;
            case "source-unload": source.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, source)); break;
            case "target-unload": target.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, target)); break;
            case "overlay": page.ShowVolumeOverlay(false, new Point(), false, false); break;
            case "reload": UiTestPump.Run(vm.LoadAsync("project.json", CancellationToken.None)); break;
            case "deactivate": UiTestPump.Run(page.DeactivateAsync()); break;
        }
        AssertEmptyPlaceholders();
        Assert.That(vm.IsNodeDragActive, Is.EqualTo(action is "source-recycle" or "target-recycle" or "source-unload" or "target-unload"));
    }

    private void BeginLaterTrackDrag()
    {
        var source = Grid(vm.Tracks[3]);
        Assert.That(page.BeginNodePointer(source, new Point(b.Timestamp / 10d, source.ActualHeight / 2), new Point()), Is.True);
    }
    private void MoveTo(Track track)
    {
        var target = Grid(track);
        page.UpdateNodePointer(new Point(200, 200), target, new Point((b.Timestamp + 250) / 10d, target.ActualHeight / 2));
    }
    private void AssertEmptyPlaceholders()
    {
        Assert.That(Grids().All(g => g.DragPreviewTimestamps is null || g.DragPreviewTimestamps.Count == 0), Is.True);
        Assert.That(vm.NodePreviewByTrack, Is.Empty); Assert.That(vm.NodePreviewTimestamps, Is.Empty);
    }

    private BeatGridView Grid(Track track) => Grids().Single(g => ReferenceEquals(g.DataContext, track));
    private IEnumerable<BeatGridView> Grids() => Descendants(page).OfType<BeatGridView>();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static Point Center(Rect bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    private static Rect[] NodeShapes(BeatGridView grid, int[] timestamps)
    {
        grid.PrepareDrawing();
        var centers = timestamps.Select(t => Center(grid.GetSampleBounds(t))).ToHashSet();
        return Shapes(grid.ContentDrawing!).Select(d => d.Geometry.Bounds)
            .Where(bounds => !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0 && centers.Contains(Center(bounds))).Distinct().ToArray();
    }
    private static IEnumerable<GeometryDrawing> Shapes(Drawing drawing)
    {
        if (drawing is GeometryDrawing geometry) yield return geometry;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var shape in Shapes(child)) yield return shape;
    }
}
