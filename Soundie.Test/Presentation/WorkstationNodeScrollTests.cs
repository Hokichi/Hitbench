using System.Windows;
using System.Windows.Controls;
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
public class WorkstationNodeScrollTests
{
    [SetUp] public void Setup() => new VolumeWheelTests().SetUp();

    private static (WorkstationPage Page, WorkstationPageVM Vm, Sample Node, BeatGridView Grid, ScrollViewer Scroll, InMemoryView View) CreateEditor()
    {
        var node = new Sample { FileName = "hit.wav", Timestamp = 1000 };
        var project = new ProjectData { Offsets = [0], BPMs = [120], WorkstationZoom = 8, Tracks = Enumerable.Range(0, 128)
            .Select(i => new Track { SelectedSampleFileName = "hit.wav", Samples = i == 0 ? [node] : [] }).ToList() };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["hit.wav"]));
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        var view = new InMemoryView(new Window { Content = page, Width = 1100, Height = 800 });
        UiTestPump.Run(page.LoadCompletion);
        vm.IsAdditionMode = false; view.UpdateLayout();
        var grid = Descendants<BeatGridView>(page).First();
        var local = new Point(1000 / 10000d * grid.ContentWidth - grid.HorizontalOffset, grid.ActualHeight / 2);
        Assert.That(page.BeginNodePointer(grid, local, grid.TranslatePoint(local, page)), Is.True);
        local.X = 1125 / 10000d * grid.ContentWidth - grid.HorizontalOffset;
        page.UpdateNodePointer(grid.TranslatePoint(local, page), grid, local);
        Assert.That(vm.IsNodeDragActive, Is.True);
        Assert.That(vm.NodePreviewTimestamps[node], Is.EqualTo(1125));
        var list = (ListBox)((Soundie.Views.Pages.OHsWorkstationTrackList)page.FindName("TrackListComponent")).FindName("TrackList");
        return (page, vm, node, grid, Descendants<ScrollViewer>(list).First(), view);
    }

    [Test]
    public void PlainWheelScrollsTrackListDuringDragAndEscapeCancels()
    {
        var editor = CreateEditor(); using var view = editor.View;
        try
        {
            var before = editor.Scroll.VerticalOffset;
            Assert.That(editor.Page.HandleContextWheel(ModifierKeys.None, -120, editor.Page), Is.True);
            view.UpdateLayout();
            Assert.That(editor.Scroll.VerticalOffset, Is.GreaterThan(before));
            Assert.That(editor.Vm.IsNodeDragActive, Is.True); Assert.That(editor.Vm.SelectedSamples, Does.Contain(editor.Node));
            Assert.That(editor.Page.HandlePlaybackKey(Key.Escape, ModifierKeys.None, false, editor.Page), Is.True);
            Assert.That(editor.Vm.IsNodeDragActive, Is.False); Assert.That(editor.Node.Timestamp, Is.EqualTo(1000));
        }
        finally { UiTestPump.Run(editor.Page.DeactivateAsync()); }
    }

    [Test]
    public void ShiftWheelRefreshesPreviewAndDropWithoutPointerMovement()
    {
        var editor = CreateEditor(); using var view = editor.View;
        try
        {
            var timeline = (ScrollViewer)(editor.Page.Timeline).FindName("WaveformScrollViewer");
            var before = timeline.HorizontalOffset; var width = editor.Grid.ContentWidth;
            Assert.That(editor.Page.HandleContextWheel(ModifierKeys.Shift, -120 * 8, editor.Page), Is.True);
            view.UpdateLayout();
            Assert.That(timeline.HorizontalOffset, Is.GreaterThan(before));
            var expected = (int)(Math.Round((1125 + (timeline.HorizontalOffset - before) / width * 10000) / 125) * 125);
            Assert.That(expected, Is.GreaterThan(1125));
            Assert.That(editor.Vm.NodePreviewTimestamps[editor.Node], Is.EqualTo(expected));
            var drop = editor.Page.CompleteNodePointerAsync(); UiTestPump.Run(drop);
            Assert.That(drop.Result, Is.True); Assert.That(editor.Node.Timestamp, Is.EqualTo(expected));
        }
        finally { UiTestPump.Run(editor.Page.DeactivateAsync()); }
    }

    [Test]
    public void SourceRecyclingKeepsOriginalModelDrag()
    {
        var editor = CreateEditor(); using var view = editor.View;
        try
        {
            var original = editor.Vm.Tracks[0];
            editor.Grid.DataContext = editor.Vm.Tracks.Last();
            Assert.That(editor.Vm.IsNodeDragActive, Is.True);
            Assert.That(editor.Vm.SelectedSamples, Does.Contain(editor.Node));
            var target = editor.Grid;
            var targetTrack = (Track)target.DataContext;
            var local = new Point(625 / 10000d * target.ContentWidth - target.HorizontalOffset, target.ActualHeight / 2);
            editor.Page.UpdateNodePointer(target.TranslatePoint(local, editor.Page), target, local);
            Assert.That(editor.Vm.NodePreviewTimestamps[editor.Node], Is.EqualTo(625));
            var drop = editor.Page.CompleteNodePointerAsync(); UiTestPump.Run(drop);
            Assert.That(drop.Result, Is.True); Assert.That(original.Samples, Is.Empty);
            Assert.That(targetTrack.Samples.Single(), Is.SameAs(editor.Node));
        }
        finally { UiTestPump.Run(editor.Page.DeactivateAsync()); }
    }

    [Test]
    public void UnloadingTargetClearsStalePreviewWithoutCancellingModelDrag()
    {
        var editor = CreateEditor(); using var view = editor.View;
        try
        {
            editor.Grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, editor.Grid));
            Assert.That(editor.Vm.IsNodeDragActive, Is.True);
            Assert.That(editor.Vm.NodePreviewTimestamps, Is.Empty);
            var drop = editor.Page.CompleteNodePointerAsync(); UiTestPump.Run(drop);
            Assert.That(drop.Result, Is.False); Assert.That(editor.Node.Timestamp, Is.EqualTo(1000));
        }
        finally { UiTestPump.Run(editor.Page.DeactivateAsync()); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T result) yield return result;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}

