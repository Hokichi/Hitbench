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
