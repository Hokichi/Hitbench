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
    [Test]
    public void UncheckedFollowingKeepsViewportWhileMarkerAdvances()
    {
        vm.ScrollWithMarker = false; UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        var viewer = (System.Windows.Controls.ScrollViewer)(page.Timeline).FindName("WaveformScrollViewer");
        viewer.ScrollToLeftEnd(); view.UpdateLayout();
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromSeconds(7) }; PumpFrame(); view.UpdateLayout();
        Assert.That(viewer.HorizontalOffset, Is.Zero);
        Assert.That(vm.Position.TotalSeconds, Is.EqualTo(7).Within(.02));
        Assert.That(((WaveformView)(page.Timeline).FindName("WaveformDisplay")).Position, Is.EqualTo(vm.Position));
    }
    [Test]
    public void CheckingDuringPlaybackRevealsMarkerWithoutAudioSeek()
    {
        var checkBox = ((osuHitsounder.Views.Pages.OHsWorkstationAudioCard)page.FindName("AudioCard")).FindName("ScrollWithMarkerCheckBox") as BalloonCheckBox;
        Assert.That(checkBox, Is.Not.Null); Assert.That(checkBox!.IsChecked, Is.True);
        Assert.That(checkBox.Style, Is.SameAs(checkBox.FindResource("OHsWorkstationScrollWithMarkerStyle")));
        Assert.That(checkBox.IsTabStop, Is.True);
        Assert.That(checkBox.FocusVisualStyle, Is.SameAs(checkBox.FindResource("OHsSampleManagerKeyboardFocusStyle")));
        checkBox.IsChecked = false; Assert.That(vm.ScrollWithMarker, Is.False);
        UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromSeconds(7) }; PumpFrame(); view.UpdateLayout();
        var before = audio.Snapshot; checkBox.IsChecked = true; view.UpdateLayout();
        var viewer = (System.Windows.Controls.ScrollViewer)(page.Timeline).FindName("WaveformScrollViewer");
        Assert.That(viewer.HorizontalOffset, Is.GreaterThan(0));
        Assert.That(audio.Snapshot, Is.EqualTo(before));
        Assert.That(page.TimelineHorizontalOffset, Is.EqualTo(viewer.HorizontalOffset));
    }
    [Test]
    public void UncheckedFollowingKeepsViewportAtNaturalCompletion()
    {
        vm.ScrollWithMarker = false; UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        var viewer = (System.Windows.Controls.ScrollViewer)(page.Timeline).FindName("WaveformScrollViewer");
        viewer.ScrollToLeftEnd(); view.UpdateLayout();
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.FromSeconds(10), false, null);
        UiTestPump.Run(StubWorkstationAudioService.WaitUntilAsync(() => vm.Position == TimeSpan.FromSeconds(10)));
        view.UpdateLayout(); Assert.That(vm.IsPlaying, Is.False); Assert.That(viewer.HorizontalOffset, Is.Zero);
    }
    [TestCase(199, 0, 0)]
    [TestCase(200, 0, 200)]
    [TestCase(650, 0, 600)]
    [TestCase(1000, 0, 800)]
    [TestCase(350, 600, 200)]
    public void MarkerPagesAcrossViewport(double marker, double offset, double expected)
    {
        Assert.That(WorkstationPage.GetPlaybackPageOffset(marker, offset, 200, 1000), Is.EqualTo(expected));
    }

    [TestCase(0)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidViewportDoesNotScroll(double viewport)
    {
        Assert.That(WorkstationPage.GetPlaybackPageOffset(650, 100, viewport, 1000), Is.EqualTo(100));
    }

    [Test]
    public void NaturalCompletionRevealsFinalMarkerWithoutAnotherFrame()
    {
        UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        var viewer = (System.Windows.Controls.ScrollViewer)(page.Timeline).FindName("WaveformScrollViewer");
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.FromSeconds(10), false, null);
        UiTestPump.Run(StubWorkstationAudioService.WaitUntilAsync(() => vm.Position == TimeSpan.FromSeconds(10)));
        view.UpdateLayout();
        Assert.That(vm.IsPlaying, Is.False);
        Assert.That(viewer.HorizontalOffset, Is.EqualTo(viewer.ScrollableWidth));
    }

    [Test]
    public void PlayingMarkerRevealsNextPageAndKeepsOffsetsAligned()
    {
        UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        var viewer = (System.Windows.Controls.ScrollViewer)(page.Timeline).FindName("WaveformScrollViewer");
        var waveform = (WaveformView)(page.Timeline).FindName("WaveformDisplay");
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromSeconds(7) };
        PumpFrame();
        view.UpdateLayout();
        Assert.That(viewer.HorizontalOffset, Is.GreaterThan(0));
        Assert.That(page.TimelineHorizontalOffset, Is.EqualTo(viewer.HorizontalOffset));
        Assert.That(waveform.HorizontalOffset, Is.EqualTo(viewer.HorizontalOffset));
        var markerX = vm.Position.TotalSeconds / 10 * waveform.ActualWidth;
        Assert.That(markerX, Is.InRange(viewer.HorizontalOffset, viewer.HorizontalOffset + viewer.ViewportWidth));
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromSeconds(7)));
    }

    [TestCase(640, FlowDirection.LeftToRight, "WASAPI")]
    [TestCase(900, FlowDirection.RightToLeft, "WASAPI")]
    [TestCase(640, FlowDirection.LeftToRight, "WaveOut")]
    [TestCase(900, FlowDirection.RightToLeft, "WaveOut")]
    public void FallbackWarningBindsAndClearsWithoutDisablingTransport(int width, FlowDirection direction, string backend)
    {
        host.Width = width;
        host.FlowDirection = direction;
        var warning = $"Using {backend} because preferred audio output failed. ASIO: unavailable. " +
            (backend == "WaveOut" ? "WASAPI: unavailable. " : "") + new string('x', 240);
        UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        audio.Snapshot = audio.Snapshot with { Warning = warning };
        PumpFrame();
        view.UpdateLayout();
        var status = (System.Windows.Controls.TextBlock)((osuHitsounder.Views.Pages.OHsWorkstationAudioCard)page.FindName("AudioCard")).FindName("PlaybackWarningText");
        Assert.That(status, Is.Not.Null);
        UiControlContract.AssertStyle<System.Windows.Controls.TextBlock>(status, "OHsWorkstationPlaybackWarningStyle");
        Assert.That(status.Text, Is.EqualTo(warning));
        Assert.That(System.Windows.Automation.AutomationProperties.GetName(status), Is.EqualTo(warning));
        Assert.That(status.Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(status.FlowDirection, Is.EqualTo(direction));
        Assert.That(vm.CanPlay, Is.True);
        Assert.That(vm.ErrorMessage, Is.Null);
        audio.Snapshot = audio.Snapshot with { Warning = null, Error = "Output failed" };
        PumpFrame();
        view.UpdateLayout();
        Assert.That(status.Visibility, Is.EqualTo(Visibility.Collapsed));
        Assert.That(vm.ErrorMessage, Is.EqualTo("Output failed"));
        audio.Snapshot = audio.Snapshot with { Warning = "" };
        PumpFrame();
        view.UpdateLayout();
        Assert.That(status.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    private StubWorkstationAudioService audio = null!;
    private osuHitsounder.Services.MarkerPlaybackCoordinator playback = null!;
    private long frameNumber = 10000;
    private WorkstationPageVM vm = null!;
    private WorkstationPage page = null!;
    private InMemoryView view = null!;
    private Window host = null!;

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
        vm = new WorkstationPageVM(source, playback = new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new osuHitsounder.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        host = new Window { Content = page, Width = 900, Height = 600 };
        view = new InMemoryView(host);
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
    }

    private void WaitForWorker()
    {
        if (audio.Disposed || !vm.IsPlaying && !audio.Snapshot.IsPlaying) return;
        var expected = audio.Snapshot;
        var clock = new osuHitsounder.Services.PlaybackMarkerClock([0,1000],[120,240]);
        var position = expected.IsPlaying ? clock.GetPosition(expected.Position, TimeSpan.FromSeconds(10)) : expected.Position;
        UiTestPump.Run(StubWorkstationAudioService.WaitUntilAsync(() =>
            playback.Latest.Playback.IsPlaying == expected.IsPlaying &&
            playback.Latest.Playback.Position >= position &&
            playback.Latest.Playback.Error == expected.Error && playback.Latest.Playback.Warning == expected.Warning));
    }

    private void PumpFrame()
    {
        WaitForWorker();
        page.ProcessPresentationFrame(TimeSpan.FromMilliseconds(++frameNumber * 16));
    }

    [TearDown]
    public void TearDown()
    {
        UiTestPump.Run(page.DeactivateAsync());
        view.Dispose();
        audio.Dispose();
    }

    [Test]
    public void RepeatedFrameDoesNotApplyNewSnapshotUntilNextFrame()
    {
        UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        page.Visibility = Visibility.Hidden;
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(12.5) };
        WaitForWorker();
        page.Visibility = Visibility.Visible;
        var frame = TimeSpan.FromHours(1);
        page.ProcessPresentationFrame(frame);
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(25) };
        // Hold the UI; the logical marker still progresses.
        Assert.That(SpinWait.SpinUntil(() => playback.Latest.Playback.Position.TotalMilliseconds >= 25, 3000), Is.True);
        page.ProcessPresentationFrame(frame);
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(12.5));
        page.ProcessPresentationFrame(frame + TimeSpan.FromMilliseconds(16));
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(25));
    }

    [TestCase(50), TestCase(100), TestCase(250)]
    public void UiStallDoesNotDelayLogicalMarker(int holdMilliseconds)
    {
        UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(holdMilliseconds) };
        Thread.Sleep(holdMilliseconds);
        Assert.That(playback.Latest.Playback.Position, Is.GreaterThan(TimeSpan.Zero));
        page.ProcessPresentationFrame(TimeSpan.FromHours(2));
        Assert.That(vm.Position, Is.EqualTo(playback.Latest.Playback.Position));
    }

    [Test]
    public void HiddenPageReceivesTerminalFailureWithoutFrames()
    {
        UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        page.Visibility = Visibility.Hidden;
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.Zero, false, "device lost");
        UiTestPump.Run(StubWorkstationAudioService.WaitUntilAsync(() => vm.ErrorMessage == "device lost"));
        Assert.That(vm.IsPlaying, Is.False);
        Assert.That(page.IsPlaybackRefreshSubscribed, Is.False);
    }

    [Test]
    public void FrameUsesLogicalMarkerAtTimingTransition()
    {
        Assert.That(page.IsPlaybackRefreshSubscribed, Is.False);
        osuHitsounder.Test.Presentation.UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        Assert.That(page.IsPlaybackRefreshSubscribed, Is.True);
        Assert.That(vm.PlaybackInterval.TotalMilliseconds, Is.EqualTo(4.1667).Within(.0001));
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(1001) };
        PumpFrame();
        Assert.That(vm.PlaybackInterval.TotalMilliseconds, Is.EqualTo(2.0833).Within(.0001));
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void FrameUpdatesBoundMarker()
    {
        osuHitsounder.Test.Presentation.UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(12.5) };
        PumpFrame();
        PumpFrame();
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(12.5));
        Assert.That(((WaveformView)(page.Timeline).FindName("WaveformDisplay")).Position, Is.EqualTo(vm.Position));
        Assert.That(playback.Latest.Playback.Position, Is.GreaterThan(TimeSpan.Zero));
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(12.5));
    }

    [TestCase(Visibility.Hidden)]
    [TestCase(Visibility.Collapsed)]
    public void HiddenPageStopsFramesButKeepsLogicalMarker(Visibility visibility)
    {
        UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        page.Visibility = visibility;
        page.UpdatePlaybackRefreshSubscription();
        Assert.That(page.IsPlaybackRefreshSubscribed, Is.False);
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(12.5) };
        WaitForWorker();
        Assert.That(playback.Latest.Playback.Position, Is.GreaterThan(TimeSpan.Zero));
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.Zero));
        page.Visibility = Visibility.Visible;
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(12.5));
    }

    [Test]
    public void PauseSeekStopAndDeactivationStopFrameWork()
    {
        osuHitsounder.Test.Presentation.UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        osuHitsounder.Test.Presentation.UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        osuHitsounder.Test.Presentation.UiTestPump.Run(vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(250)));
        var reads = audio.SnapshotReads;
        PumpFrame();
        Assert.That(audio.SnapshotReads, Is.EqualTo(reads));
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(250));
        osuHitsounder.Test.Presentation.UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        Assert.That(page.IsPlaybackRefreshSubscribed, Is.True);
        osuHitsounder.Test.Presentation.UiTestPump.Run(vm.StopCommand.ExecuteAsync(null));
        Assert.That(page.IsPlaybackRefreshSubscribed, Is.False);
        osuHitsounder.Test.Presentation.UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        UiTestPump.Run(page.DeactivateAsync());
        reads = audio.SnapshotReads;
        page.UpdatePlaybackRefreshSubscription();
        PumpFrame();
        Assert.That(page.IsPlaybackRefreshSubscribed, Is.False);
        Assert.That(audio.SnapshotReads, Is.EqualTo(reads));
    }

    [TestCase(null)]
    [TestCase("device lost")]
    public void FinalSnapshotStopsFramesAndRetainsError(string? error)
    {
        osuHitsounder.Test.Presentation.UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        audio.Snapshot = new AudioPlaybackSnapshot(error is null ? TimeSpan.FromSeconds(10) : TimeSpan.Zero, false, error);
        PumpFrame();
        Assert.That(page.IsPlaybackRefreshSubscribed, Is.False);
        if (error is not null) Assert.That(vm.ErrorMessage, Is.EqualTo(error));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LoadingOrFailedLoadDoesNotEnablePolling(bool fail)
    {
        var completion = new TaskCompletionSource<WorkstationSource>();
        using var loadingAudio = new StubWorkstationAudioService();
        var loadingVm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => completion.Task }, new osuHitsounder.Services.MarkerPlaybackCoordinator(loadingAudio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var loadingPage = new WorkstationPage(loadingVm);
        loadingPage.SetProjectPath("loading.json");
        using var loadingView = new InMemoryView(new Window { Content = loadingPage, Width = 640, Height = 480 });
        try
        {
            loadingPage.UpdatePlaybackRefreshSubscription();
            Assert.That(loadingVm.CanPlay, Is.False);
            if (fail) completion.SetException(new IOException("Cannot load song"));
            else completion.SetCanceled();
            UiTestPump.Run(loadingPage.LoadCompletion);
            loadingPage.UpdatePlaybackRefreshSubscription();
            Assert.That(loadingPage.IsPlaybackRefreshSubscribed, Is.False);
        }
        finally { UiTestPump.Run(loadingPage.DeactivateAsync()); }
    }
}
