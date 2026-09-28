using System.Windows;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationPlaybackRefreshTests
{
    private StubWorkstationAudioService audio = null!;
    private WorkstationPageVM vm = null!;
    private WorkstationPage page = null!;
    private InMemoryView view = null!;

    [SetUp]
    public void SetUp()
    {
        new WorkstationPageIntegrationTests().SetUp();
        audio = new StubWorkstationAudioService();
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData
            {
                Offsets = [0, 1000], BPMs = [120, 240],
                Tracks = [new Track { Samples = [new Sample { FileName = "hit.wav", Timestamp = 10 }] }]
            }))
        };
        vm = new WorkstationPageVM(source, audio, new ImmediateUiScheduler());
        page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
    }

    [TearDown]
    public void TearDown()
    {
        UiTestPump.Run(page.DeactivateAsync());
        view.Dispose();
        audio.Dispose();
    }

    [Test]
    public void TimerUsesBeatIntervalAndUpdatesAtTimingTransition()
    {
        Assert.That(page.IsPlaybackTimerEnabled, Is.False);
        vm.PlayPauseCommand.Execute(null);
        Assert.That(page.IsPlaybackTimerEnabled, Is.True);
        Assert.That(page.PlaybackTimerInterval.TotalMilliseconds, Is.EqualTo(4.1667).Within(.0001));
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(1001) };
        page.OnPlaybackTick(null, EventArgs.Empty);
        Assert.That(page.PlaybackTimerInterval.TotalMilliseconds, Is.EqualTo(2.0833).Within(.0001));
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void TickUpdatesBoundMarkerAndTriggersCrossedSampleOnce()
    {
        vm.PlayPauseCommand.Execute(null);
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(12.5) };
        page.OnPlaybackTick(null, EventArgs.Empty);
        page.OnPlaybackTick(null, EventArgs.Empty);
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(12.5));
        Assert.That(((WaveformView)page.FindName("WaveformDisplay")).Position, Is.EqualTo(vm.Position));
        Assert.That(audio.TriggerBatches.SelectMany(batch => batch), Is.EqualTo(new long[] { 1 }));
    }

    [TestCase(Visibility.Hidden)]
    [TestCase(Visibility.Collapsed)]
    public void HiddenPageKeepsFunctionalTimerWithoutAnimationRefreshPath(Visibility visibility)
    {
        vm.PlayPauseCommand.Execute(null);
        page.Visibility = visibility;
        page.UpdatePlaybackRefreshSubscription();
        Assert.That(page.IsPlaybackTimerEnabled, Is.True);
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(12.5) };
        page.OnPlaybackTick(null, EventArgs.Empty);
        Assert.That(audio.TriggerBatches, Has.Count.EqualTo(1));
        page.Visibility = Visibility.Visible;
        Assert.That(audio.TriggerBatches, Has.Count.EqualTo(1));
    }

    [Test]
    public void PauseSeekStopAndDeactivationStopTimerWork()
    {
        vm.PlayPauseCommand.Execute(null);
        vm.PlayPauseCommand.Execute(null);
        vm.SeekCommand.Execute(TimeSpan.FromMilliseconds(250));
        var reads = audio.SnapshotReads;
        page.OnPlaybackTick(null, EventArgs.Empty);
        Assert.That(audio.SnapshotReads, Is.EqualTo(reads));
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(250));
        vm.PlayPauseCommand.Execute(null);
        Assert.That(page.IsPlaybackTimerEnabled, Is.True);
        vm.StopCommand.Execute(null);
        Assert.That(page.IsPlaybackTimerEnabled, Is.False);
        vm.PlayPauseCommand.Execute(null);
        UiTestPump.Run(page.DeactivateAsync());
        reads = audio.SnapshotReads;
        page.UpdatePlaybackRefreshSubscription();
        page.OnPlaybackTick(null, EventArgs.Empty);
        Assert.That(page.IsPlaybackTimerEnabled, Is.False);
        Assert.That(audio.SnapshotReads, Is.EqualTo(reads));
    }

    [TestCase(null)]
    [TestCase("device lost")]
    public void FinalSnapshotStopsTimerAndRetainsError(string? error)
    {
        vm.PlayPauseCommand.Execute(null);
        audio.Snapshot = new AudioPlaybackSnapshot(error is null ? TimeSpan.FromSeconds(10) : TimeSpan.Zero, false, error);
        page.OnPlaybackTick(null, EventArgs.Empty);
        Assert.That(page.IsPlaybackTimerEnabled, Is.False);
        if (error is not null) Assert.That(vm.ErrorMessage, Is.EqualTo(error));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LoadingOrFailedLoadDoesNotEnablePolling(bool fail)
    {
        var completion = new TaskCompletionSource<WorkstationSource>();
        using var loadingAudio = new StubWorkstationAudioService();
        var loadingVm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => completion.Task },
            loadingAudio, new ImmediateUiScheduler());
        var loadingPage = new WorkstationPage(loadingVm);
        loadingPage.SetProjectPath("loading.json");
        using var loadingView = new InMemoryView(new Window { Content = loadingPage, Width = 640, Height = 480 });
        try
        {
            loadingPage.UpdatePlaybackRefreshSubscription();
            Assert.That(loadingPage.IsPlaybackTimerEnabled, Is.False);
            if (fail) completion.SetException(new IOException("Cannot load song"));
            else completion.SetCanceled();
            UiTestPump.Run(loadingPage.LoadCompletion);
            loadingPage.UpdatePlaybackRefreshSubscription();
            Assert.That(loadingPage.IsPlaybackTimerEnabled, Is.False);
        }
        finally { UiTestPump.Run(loadingPage.DeactivateAsync()); }
    }
}
