using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class WorkstationReadinessTests
{
    [TestCase(0), TestCase(128), Apartment(ApartmentState.STA), NonParallelizable]
    public void ReadinessDoesNotWaitForOffscreenTracks(int count)
    {
        new WorkstationPageIntegrationTests().SetUp();
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new osuHitsounder.Core.Entities.WorkstationSource("map", "song", new osuHitsounder.Core.Entities.ProjectData
            {
                Offsets = [0], BPMs = [120], Tracks = Enumerable.Range(0, count).Select(_ => new osuHitsounder.Core.Entities.Track()).ToList()
            }))
        };
        var vm = new WorkstationPageVM(source,
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync),
            new osuHitsounder.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var page = new osuHitsounder.Views.Pages.WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new System.Windows.Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        Assert.That(vm.CanPlay, Is.True);
        var list = (System.Windows.Controls.ItemsControl)page.FindName("TrackList");
        if (count > 0)
        {
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(0), Is.Not.Null);
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(count - 1), Is.Null);
        }
        UiTestPump.Run(page.DeactivateAsync());
    }

    [Test]
    public async Task AlreadyCancelledLoadReachesTerminalState()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync), new ImmediateUiScheduler());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await vm.LoadAsync("project.json", cancellation.Token);
            Assert.That(vm.LoadPhase, Is.EqualTo(ProjectLoadPhase.Cancelled));
            Assert.That(vm.IsLoading, Is.False);
        }
        finally { await vm.DeactivateAsync(); }
    }
    [TestCase(640, System.Windows.FlowDirection.LeftToRight), TestCase(900, System.Windows.FlowDirection.RightToLeft)]
    [Apartment(ApartmentState.STA), NonParallelizable]
    public void OverlayWaitsForTwoDistinctFrames(int width, System.Windows.FlowDirection direction)
    {
        new WorkstationPageIntegrationTests().SetUp();
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)),
            new osuHitsounder.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var page = new osuHitsounder.Views.Pages.WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new System.Windows.Window { Content = page, Width = width, Height = 480, FlowDirection = direction }, false);
        page.Visibility = System.Windows.Visibility.Hidden;
        UiTestPump.Run(StubWorkstationAudioService.WaitUntilAsync(() => vm.LoadPhase == ProjectLoadPhase.AwaitingPresentation));
        view.UpdateLayout();
        var overlay = (System.Windows.Controls.Border)page.FindName("LoadingOverlay");
        var toast = (System.Windows.Controls.TextBlock)page.FindName("LoadingProjectText");
        Assert.That(overlay.Visibility, Is.EqualTo(System.Windows.Visibility.Visible));
        Assert.That(toast.Text, Is.EqualTo("Loading Project..."));
        Assert.That(toast.FlowDirection, Is.EqualTo(direction));
        toast.Text = new string('w', 500);
        view.UpdateLayout();
        Assert.That(toast.ActualWidth, Is.LessThan(width));
        Assert.That(toast.ActualHeight, Is.GreaterThan(26));
        toast.Text = "Loading Project...";
        Assert.That(vm.CanPlay, Is.False);
        page.Visibility = System.Windows.Visibility.Visible;
        view.UpdateLayout();
        page.ProcessPresentationFrame(TimeSpan.FromMilliseconds(1));
        page.ProcessPresentationFrame(TimeSpan.FromMilliseconds(1));
        Assert.That(vm.CanPlay, Is.False);
        page.ProcessPresentationFrame(TimeSpan.FromMilliseconds(2));
        UiTestPump.Run(page.LoadCompletion);
        Assert.That(vm.CanPlay, Is.True);
        Assert.That(overlay.Visibility, Is.EqualTo(System.Windows.Visibility.Collapsed));
        UiTestPump.Run(page.DeactivateAsync());
        Assert.That(page.IsPlaybackRefreshSubscribed, Is.False);
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable]
    public void NavigationCancelsPresentationWaitWithoutAnotherFrame()
    {
        new WorkstationPageIntegrationTests().SetUp();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new osuHitsounder.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var page = new osuHitsounder.Views.Pages.WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new System.Windows.Window { Content = page, Width = 640, Height = 480 }, false);
        page.Visibility = System.Windows.Visibility.Hidden;
        UiTestPump.Run(StubWorkstationAudioService.WaitUntilAsync(() => vm.LoadPhase == ProjectLoadPhase.AwaitingPresentation));
        var loading = page.LoadCompletion;
        Assert.That(loading.IsCompleted, Is.False);
        UiTestPump.Run(page.DeactivateAsync());
        UiTestPump.Run(loading);
        Assert.That(vm.LoadPhase, Is.EqualTo(ProjectLoadPhase.Cancelled));
        Assert.That(page.IsPlaybackRefreshSubscribed, Is.False);
    }

    [Test]
    public async Task LoadingPersistsUntilCurrentPresentationAcknowledged()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        try
        {
            await vm.LoadAsync("project.json", default);
            Assert.That(vm.LoadPhase, Is.EqualTo(ProjectLoadPhase.AwaitingPresentation));
            Assert.That(vm.IsLoading, Is.True);
            Assert.That(vm.CanPlay, Is.False);
            Assert.That(vm.IncreaseSubdivisionCommand.CanExecute(null), Is.False);
            await vm.PlayPauseCommand.ExecuteAsync(null);
            Assert.That(audio.Snapshot.IsPlaying, Is.False);
            vm.AcknowledgePresentationReady(vm.LoadGeneration - 1);
            Assert.That(vm.CanPlay, Is.False);
            vm.AcknowledgePresentationReady(vm.LoadGeneration);
            Assert.That(vm.IsLoading, Is.False);
            Assert.That(vm.CanPlay, Is.True);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task FailedPreparationClearsLoadingWithoutEnablingPlayback()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => throw new IOException("bad project") },
            new MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        try
        {
            await vm.LoadAsync("project.json", default);
            Assert.That(vm.LoadPhase, Is.EqualTo(ProjectLoadPhase.Failed));
            Assert.That(vm.IsLoading, Is.False);
            Assert.That(vm.CanPlay, Is.False);
        }
        finally { await vm.DeactivateAsync(); }
    }
}
