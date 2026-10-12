using System.Windows;
using System.Windows.Controls;
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
public class WorkstationRegionSelectionTests
{
    private WorkstationPage page = null!;
    private WorkstationPageVM vm = null!;
    private InMemoryView view = null!;
    private BeatGridView[] grids = [];
    private Sample first = null!, second = null!;

    [SetUp]
    public void Setup()
    {
        new VolumeWheelTests().SetUp();
        first = new Sample { Timestamp = 1000, FileName = "a.wav" };
        second = new Sample { Timestamp = 1500, FileName = "b.wav" };
        var data = new ProjectData { Offsets = [0], BPMs = [120], Tracks =
            [new Track { SelectedSampleFileName = "a.wav", Samples = [first] },
             new Track { SelectedSampleFileName = "b.wav", Samples = [second] }] };
        vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", data)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            new StubSampleFileService(["a.wav", "b.wav"]), nodePasteDialog: new StubNodePasteDialogService());
        page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        view = new InMemoryView(new Window { Content = page, Width = 1100, Height = 800 });
        UiTestPump.Run(page.LoadCompletion); view.UpdateLayout();
        grids = Descendants<BeatGridView>(page).ToArray();
        foreach (var grid in grids) { grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.HorizontalOffset = 0; }
    }
    [TearDown]
    public void Cleanup() { UiTestPump.Run(page.DeactivateAsync()); view.Dispose(); }

    private Point PagePoint(int row, double x, double y) => grids[row].TranslatePoint(new Point(x, y), page);
    private void StartBox(bool reverse = false, double left = 40, double right = 250)
    {
        var start = reverse ? PagePoint(1, right, grids[1].ActualHeight - 1) : PagePoint(0, left, 1);
        var end = reverse ? PagePoint(0, left, 1) : PagePoint(1, right, grids[1].ActualHeight - 1);
        var row = reverse ? 1 : 0;
        Assert.That(page.BeginNodePointer(grids[row], page.TranslatePoint(start, grids[row]), start), Is.True);
        page.UpdateNodePointer(end, grids[1 - row], page.TranslatePoint(end, grids[1 - row]));
    }
    private void Release() => UiTestPump.Run(page.CompleteNodePointerAsync());
    private void AssertNoAdorner()
    {
        var list = (ListBox)page.FindName("TrackList");
        Assert.That(AdornerLayer.GetAdornerLayer(list)!.GetAdorners(list), Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RectangleAcrossTracksSelectsIntersectedNodes(bool reverse)
    {
        StartBox(reverse);
        Assert.That(page.IsRegionSelectionActive, Is.True);
        Assert.That(vm.SelectedSamples, Is.EquivalentTo(new[] { first, second }));
        Assert.That(vm.IsNodeDragActive, Is.False); Assert.That(vm.CanUndo, Is.False);
        var list = (ListBox)page.FindName("TrackList");
        var adorner = AdornerLayer.GetAdornerLayer(list)!.GetAdorners(list)!.Single();
        Assert.That(adorner, Is.TypeOf<OHsSelectionRegionAdorner>());
        Assert.That(adorner.IsHitTestVisible, Is.False);
        Release(); Assert.That(page.IsRegionSelectionActive, Is.False); AssertNoAdorner();
        Assert.That(vm.SelectedSamples, Is.EquivalentTo(new[] { first, second }));
        Assert.That(vm.Tracks.Sum(t => t.Samples.Count), Is.EqualTo(2));
    }
    [Test]
    public void PartialIntersectionSelectsNode()
    {
        StartBox(right: grids[0].GetSampleBounds(first.Timestamp).Left + 1);
        Assert.That(vm.SelectedSamples.Single(), Is.SameAs(first));
    }
    [Test]
    public void RegionClipsToHorizontalViewport()
    {
        var offscreen = new Sample { Timestamp = 9000, FileName = "b.wav" }; vm.Tracks[1].Samples.Add(offscreen);
        StartBox(right: 2000);
        Assert.That(vm.SelectedSamples, Is.EquivalentTo(new[] { first, second }));
    }
    [Test]
    public void RectangleUsesCurrentHorizontalOffsetAndScale()
    {
        foreach (var grid in grids) { grid.ContentWidth = 2000; grid.HorizontalOffset = 180; }
        StartBox(left: 5, right: 160);
        Assert.That(vm.SelectedSamples, Is.EquivalentTo(new[] { first, second }));
    }
    [TestCase(true)]
    [TestCase(false)]
    public void BlankClickPlacesOnlyInAdditionMode(bool addition)
    {
        vm.IsAdditionMode = addition; vm.SetNodeSelection([first]);
        var p = PagePoint(0, 300, grids[0].ActualHeight / 2);
        Assert.That(page.BeginNodePointer(grids[0], new Point(300, grids[0].ActualHeight / 2), p), Is.True);
        Assert.That(vm.Tracks[0].Samples.Count, Is.EqualTo(1));
        Release(); Assert.That(vm.Tracks[0].Samples.Count, Is.EqualTo(addition ? 2 : 1));
        if (addition) Assert.That(vm.Tracks[0].Samples.Last().Timestamp, Is.EqualTo(3000));
        else Assert.That(vm.SelectedSamples, Is.Empty);
    }
    [Test]
    public void EmptyBoxReturningToOriginNeverPlacesNode()
    {
        vm.SetNodeSelection([first]); StartBox(left: 300, right: 400);
        Assert.That(vm.SelectedSamples, Is.Empty);
        var origin = PagePoint(0, 300, 1);
        page.UpdateNodePointer(origin, grids[0], new Point(300, 1));
        Release(); Assert.That(vm.Tracks.Sum(t => t.Samples.Count), Is.EqualTo(2));
        Assert.That(vm.CanUndo, Is.False);
    }
    [TestCase("selected")]
    [TestCase("unselected")]
    [TestCase("blank")]
    public void CtrlDragDoesNotToggleOrMove(string origin)
    {
        vm.SetNodeSelection([second]);
        var row = origin == "selected" ? 1 : 0;
        var x = origin == "blank" ? 300 : origin == "selected" ? 150 : 100;
        var p = PagePoint(row, x, grids[row].ActualHeight / 2);
        Assert.That(page.BeginNodePointer(grids[row], new Point(x, grids[row].ActualHeight / 2), p, ModifierKeys.Control), Is.True);
        page.UpdateNodePointer(new Point(p.X + 100, p.Y + 100), grids[row], new Point(x + 100, grids[row].ActualHeight / 2), ModifierKeys.Control);
        Release(); Assert.That(vm.SelectedSamples.Single(), Is.SameAs(second));
        Assert.That(new[] { first.Timestamp, second.Timestamp }, Is.EqualTo(new[] { 1000, 1500 }));
        Assert.That(vm.CanUndo, Is.False); AssertNoAdorner();
    }
    [Test]
    public void CtrlClickTogglesOnlyAfterRelease()
    {
        vm.SetNodeSelection([second]); var p = PagePoint(0, 100, grids[0].ActualHeight / 2);
        page.BeginNodePointer(grids[0], new Point(100, grids[0].ActualHeight / 2), p, ModifierKeys.Control);
        Assert.That(vm.SelectedSamples.Single(), Is.SameAs(second));
        Release(); Assert.That(vm.SelectedSamples, Is.EquivalentTo(new[] { first, second }));
        page.BeginNodePointer(grids[0], new Point(100, grids[0].ActualHeight / 2), p, ModifierKeys.Control);
        Release(); Assert.That(vm.SelectedSamples.Single(), Is.SameAs(second));
    }
    [Test]
    public void IntroducingCtrlCancelsMovementWithoutCommit()
    {
        var p = PagePoint(0, 100, grids[0].ActualHeight / 2);
        page.BeginNodePointer(grids[0], new Point(100, grids[0].ActualHeight / 2), p);
        page.UpdateNodePointer(new Point(p.X + 50, p.Y), grids[0], new Point(150, grids[0].ActualHeight / 2));
        Assert.That(vm.IsNodeDragActive, Is.True);
        page.UpdateNodePointer(new Point(p.X + 100, p.Y), grids[0], new Point(200, grids[0].ActualHeight / 2), ModifierKeys.Control);
        Release(); Assert.That(first.Timestamp, Is.EqualTo(1000)); Assert.That(vm.CanUndo, Is.False);
        Assert.That(vm.SelectedSamples.Single(), Is.SameAs(first)); AssertNoAdorner();
    }
    [TestCase(false)]
    [TestCase(true)]
    public void EscapeKeepsSelectionAndRepeatedKeyDoesNotClear(bool moveNodes)
    {
        if (moveNodes)
        {
            vm.SetNodeSelection([first, second]); var p = PagePoint(0, 100, grids[0].ActualHeight / 2);
            page.BeginNodePointer(grids[0], new Point(100, grids[0].ActualHeight / 2), p);
            page.UpdateNodePointer(new Point(p.X + 50, p.Y), grids[0], new Point(150, grids[0].ActualHeight / 2));
        }
        else StartBox();
        Assert.That(page.HandlePlaybackKey(Key.Escape, ModifierKeys.None, false, grids[0]), Is.True);
        Assert.That(vm.SelectedSamples, Is.EquivalentTo(new[] { first, second }));
        Assert.That(page.IsRegionSelectionActive, Is.False); Assert.That(vm.IsNodeDragActive, Is.False);
        page.HandlePlaybackKey(Key.Escape, ModifierKeys.None, true, grids[0]);
        Assert.That(vm.SelectedSamples, Is.EquivalentTo(new[] { first, second }));
        Release(); Assert.That(first.Timestamp, Is.EqualTo(1000)); AssertNoAdorner();
        page.HandlePlaybackKey(Key.Escape, ModifierKeys.None, false, grids[0]);
        Assert.That(vm.SelectedSamples, Is.Empty);
    }
    [TestCase("capture")]
    [TestCase("recycle")]
    [TestCase("unload")]
    [TestCase("overlay")]
    [TestCase("deactivate")]
    [TestCase("disabled")]
    [TestCase("invalid")]
    public void CancelledBoxDoesNotCommitPlacement(string reason)
    {
        StartBox();
        switch (reason)
        {
            case "capture": page.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent }); break;
            case "recycle": grids[0].DataContext = new Track(); break;
            case "unload": grids[0].RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); break;
            case "overlay": page.ShowVolumeOverlay(false, new Point(), false, false); break;
            case "deactivate": UiTestPump.Run(page.DeactivateAsync()); break;
            case "disabled": vm.IsReturning = true; break;
            case "invalid": page.UpdateNodePointer(new Point(double.NaN, 0), null, default); break;
        }
        Assert.That(page.IsRegionSelectionActive, Is.False); Release(); AssertNoAdorner();
        Assert.That(vm.Tracks.Sum(t => t.Samples.Count), Is.EqualTo(2)); Assert.That(vm.CanUndo, Is.False);
        if (reason != "deactivate") Assert.That(vm.SelectedSamples, Is.EquivalentTo(new[] { first, second }));
    }
    [Test]
    public void RectangleDoesNotSelectTracksBelowViewportOrForceRealization()
    {
        for (var i = 0; i < 40; i++) vm.Project!.Tracks.Add(new Track { SelectedSampleFileName = "a.wav", Samples = [new Sample { Timestamp = 1000, FileName = "a.wav" }] });
        UiTestPump.Run(vm.LoadAsync("project.json", default)); view.UpdateLayout();
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        grids = Descendants<BeatGridView>(page).ToArray();
        foreach (var grid in grids) { grid.ContentWidth = 1000; grid.ViewportWidth = 500; }
        var p = PagePoint(0, 40, 1);
        page.BeginNodePointer(grids[0], new Point(40, 1), p);
        page.UpdateNodePointer(new Point(p.X + 210, p.Y + 10000), null, default);
        Assert.That(vm.SelectedSamples.Contains(first), Is.True);
        Assert.That(vm.SelectedSamples.Contains(vm.Tracks.Last().Samples.Single()), Is.False);
        Assert.That(((ListBox)page.FindName("TrackList")).ItemContainerGenerator.ContainerFromItem(vm.Tracks.Last()), Is.Null);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var foundChild in Descendants<T>(child)) yield return foundChild;
        }
    }
}
