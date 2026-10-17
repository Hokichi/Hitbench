using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationTrackScrollTests
{
    [SetUp] public void Setup() => new VolumeWheelTests().SetUp();

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void AddingTrackScrollsToBottomWithoutHorizontalMovement(bool instrument, bool collapsed)
    {
        var messenger = new WeakReferenceMessenger();
        var (page, vm, view) = Create(collapsed, messenger);
        using (view)
        try
        {
            var scroll = Descendants<ScrollViewer>((ListBox)((osuHitsounder.Views.Pages.OHsWorkstationTrackList)page.FindName("TrackListComponent")).FindName("TrackList")).First();
            Assert.That(scroll.ScrollableHeight, Is.GreaterThan(0));
            Assert.That(scroll.VerticalOffset, Is.Zero, "Loading must not auto-scroll");
            var horizontal = page.TimelineHorizontalOffset;
            for (var i = 0; i < 3; i++)
                if (instrument) messenger.Send(new AddPianoTrackRequestedMessage());
                else messenger.Send(new AddSampleTrackRequestedMessage());
            Settle(page, view);
            Assert.That(scroll.VerticalOffset, Is.EqualTo(scroll.ScrollableHeight).Within(.01));
            Assert.That(page.TimelineHorizontalOffset, Is.EqualTo(horizontal));
            Assert.That(vm.SelectedTrack, Is.SameAs(vm.Tracks[^1]));
            Assert.That(vm.AreTracksCollapsed, Is.EqualTo(collapsed));
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    [Test]
    public void QueuedAdditionDoesNotScrollReplacementLoadOrRemoval()
    {
        var (page, vm, view) = Create(false, new WeakReferenceMessenger());
        using (view)
        try
        {
            var scroll = Descendants<ScrollViewer>((ListBox)((osuHitsounder.Views.Pages.OHsWorkstationTrackList)page.FindName("TrackListComponent")).FindName("TrackList")).First();
            vm.AddSampleTrackCommand.Execute(null);
            UiTestPump.Run(vm.LoadAsync("next.json", default));
            Settle(page, view);
            Assert.That(scroll.VerticalOffset, Is.Zero);
            vm.AcknowledgePresentationReady(vm.LoadGeneration);
            UiTestPump.Run(vm.RemoveTrackCommand.ExecuteAsync(vm.Tracks[^1]));
            Settle(page, view);
            Assert.That(scroll.VerticalOffset, Is.Zero);
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    [Test]
    public void QueuedAdditionDoesNotScrollAfterDeactivation()
    {
        var (page, vm, view) = Create(false, new WeakReferenceMessenger());
        using (view)
        {
            var scroll = Descendants<ScrollViewer>((ListBox)((osuHitsounder.Views.Pages.OHsWorkstationTrackList)page.FindName("TrackListComponent")).FindName("TrackList")).First();
            vm.AddSampleTrackCommand.Execute(null);
            UiTestPump.Run(page.DeactivateAsync());
            Settle(page, view);
            Assert.That(scroll.VerticalOffset, Is.Zero);
        }
    }

    private static (WorkstationPage, WorkstationPageVM, InMemoryView) Create(bool collapsed, IMessenger messenger)
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120],
            Tracks = Enumerable.Range(0, 60).Select(i => new Track { IsCollapsed = collapsed, Name = $"Track {i}", SelectedSampleFileName = "hit.wav" }).ToList() };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) };
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), new StubSampleFileService(["hit.wav"]), messenger: messenger);
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion); Settle(page, view);
        return (page, vm, view);
    }

    private static void Settle(WorkstationPage page, InMemoryView view)
    {
        view.UpdateLayout();
        UiTestPump.Run(page.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
        view.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }
}

