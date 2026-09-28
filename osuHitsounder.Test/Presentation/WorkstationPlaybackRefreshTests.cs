using System.Diagnostics;
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
        vm = new WorkstationPageVM(new StubWorkstationSourceService(), audio, new ImmediateUiScheduler());
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

    private void Play()
    {
        if (!vm.IsPlaying) vm.PlayPauseCommand.Execute(null);
        page.UpdatePlaybackRefreshSubscription(true);
    }

    [TestCase(60)]
    [TestCase(120)]
    [TestCase(144)]
    public void DistinctFramesFollowDevicePositionAndDuplicateFramesDoNotPoll(int fps)
    {
        Play();
        var reads = audio.SnapshotReads;
        for (var i = 0; i < 8; i++)
        {
            audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.FromMilliseconds(1234 + i), true, null);
            var frame = TimeSpan.FromSeconds(i / (double)fps);
            page.RefreshPlaybackFrame(frame);
            audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromSeconds(9) };
            page.RefreshPlaybackFrame(frame);
            Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(1234 + i)));
        }
        Assert.That(audio.SnapshotReads - reads, Is.EqualTo(8));
        Assert.That(((WaveformView)page.FindName("WaveformDisplay")).Position, Is.EqualTo(vm.Position));
    }

    [Test]
    public void PauseSeekResumeAndStopKeepImmediatePositionsAndResetFrameDeduplication()
    {
        Play();
        page.RefreshPlaybackFrame(TimeSpan.Zero);
        vm.PlayPauseCommand.Execute(null);
        vm.SeekCommand.Execute(TimeSpan.FromMilliseconds(250));
        var reads = audio.SnapshotReads;
        page.RefreshPlaybackFrame(TimeSpan.FromSeconds(1));
        page.OnPlaybackTick(null, EventArgs.Empty);
        Assert.That(audio.SnapshotReads, Is.EqualTo(reads));
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
        Play();
        page.RefreshPlaybackFrame(TimeSpan.Zero);
        Assert.That(audio.SnapshotReads, Is.GreaterThan(reads));
        vm.StopCommand.Execute(null);
        reads = audio.SnapshotReads;
        page.RefreshPlaybackFrame(TimeSpan.FromSeconds(2));
        Assert.That(audio.SnapshotReads, Is.EqualTo(reads));
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.Zero));
    }

    [TestCase(null)]
    [TestCase("device lost")]
    public void FinalSnapshotStopsPollingAndRetainsError(string? error)
    {
        Play();
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.Zero, false, error);
        page.RefreshPlaybackFrame(TimeSpan.Zero);
        var reads = audio.SnapshotReads;
        page.RefreshPlaybackFrame(TimeSpan.FromSeconds(1));
        page.OnPlaybackTick(null, EventArgs.Empty);
        Assert.That(audio.SnapshotReads, Is.EqualTo(reads));
        Assert.That(vm.IsPlaying, Is.False);
        if (error is not null) Assert.That(vm.ErrorMessage, Is.EqualTo(error));
    }

    [Test]
    public void HiddenPageUsesFallbackAndRevealImmediatelyRefreshesPosition()
    {
        Play();
        page.Visibility = Visibility.Hidden;
        var reads = audio.SnapshotReads;
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromSeconds(2) };
        page.RefreshPlaybackFrame(TimeSpan.Zero);
        Assert.That(audio.SnapshotReads, Is.EqualTo(reads));
        page.OnPlaybackTick(null, EventArgs.Empty);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromSeconds(2)));
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromSeconds(3) };
        page.Visibility = Visibility.Visible;
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromSeconds(3)));
        page.UpdatePlaybackRefreshSubscription(true);
        reads = audio.SnapshotReads;
        page.OnPlaybackTick(null, EventArgs.Empty);
        Assert.That(audio.SnapshotReads, Is.EqualTo(reads));
    }

    [Test]
    public void ReducedMotionSwitchesBetweenTimerAndRenderingWithoutDuplicateWork()
    {
        Play();
        page.UpdatePlaybackRefreshSubscription(false);
        page.UpdatePlaybackRefreshSubscription(false);
        var reads = audio.SnapshotReads;
        page.RefreshPlaybackFrame(TimeSpan.Zero);
        Assert.That(audio.SnapshotReads, Is.EqualTo(reads));
        page.OnPlaybackTick(null, EventArgs.Empty);
        Assert.That(audio.SnapshotReads, Is.EqualTo(reads + 1));
        page.UpdatePlaybackRefreshSubscription(true);
        page.UpdatePlaybackRefreshSubscription(true);
        page.RefreshPlaybackFrame(TimeSpan.Zero);
        page.OnPlaybackTick(null, EventArgs.Empty);
        Assert.That(audio.SnapshotReads, Is.EqualTo(reads + 2));
    }

    [Test]
    public void DeactivationStopsBothRefreshPaths()
    {
        Play();
        UiTestPump.Run(page.DeactivateAsync());
        var reads = audio.SnapshotReads;
        page.UpdatePlaybackRefreshSubscription(true);
        page.UpdatePlaybackRefreshSubscription(false);
        page.RefreshPlaybackFrame(TimeSpan.Zero);
        page.OnPlaybackTick(null, EventArgs.Empty);
        Assert.That(audio.SnapshotReads, Is.EqualTo(reads));
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
            loadingAudio.Snapshot = loadingAudio.Snapshot with { IsPlaying = true };
            loadingVm.RefreshPlayback();
            loadingPage.UpdatePlaybackRefreshSubscription(true);
            var reads = loadingAudio.SnapshotReads;
            loadingPage.RefreshPlaybackFrame(TimeSpan.Zero);
            loadingPage.OnPlaybackTick(null, EventArgs.Empty);
            Assert.That(loadingAudio.SnapshotReads, Is.EqualTo(reads));
            if (fail) completion.SetException(new IOException("Cannot load song"));
            else completion.SetCanceled();
            try { UiTestPump.Run(loadingPage.LoadCompletion); }
            catch (OperationCanceledException) { }
            loadingPage.UpdatePlaybackRefreshSubscription(true);
            reads = loadingAudio.SnapshotReads;
            loadingPage.RefreshPlaybackFrame(TimeSpan.FromSeconds(1));
            loadingPage.OnPlaybackTick(null, EventArgs.Empty);
            Assert.That(loadingAudio.SnapshotReads, Is.EqualTo(reads));
        }
        finally { UiTestPump.Run(loadingPage.DeactivateAsync()); }
    }

    [TestCase(1)]
    [TestCase(16)]
    [TestCase(64)]
    public void RefreshCostProbe(int trackCount)
    {
        for (var i = 0; i < trackCount; i++) vm.Tracks.Add(new Track());
        view.UpdateLayout();
        Play();
        var notifications = 0;
        vm.PropertyChanged += (_, _) => notifications++;
        for (var i = 0; i < 100; i++) page.RefreshPlaybackFrame(TimeSpan.FromTicks(i));
        var reads = audio.SnapshotReads;
        notifications = 0;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 100; i < 1100; i++)
        {
            audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(i) };
            page.RefreshPlaybackFrame(TimeSpan.FromTicks(i));
            page.RefreshPlaybackFrame(TimeSpan.FromTicks(i));
        }
        watch.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.That(audio.SnapshotReads - reads, Is.EqualTo(1000));
        TestContext.Out.WriteLine($"tracks={trackCount}; 1000 distinct+1000 duplicate callbacks; ms={watch.Elapsed.TotalMilliseconds:F3}; bytes={bytes}; notifications={notifications}; snapshotReads={audio.SnapshotReads - reads}. Includes test input allocations; excludes rendered/GPU work.");
    }
}
