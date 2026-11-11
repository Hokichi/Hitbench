using Hitbench.Test;
using Hitbench.Services;
using Hitbench.Test.Presentation;

namespace Hitbench.Test.Services;

public class MarkerPlaybackCoordinatorTests
{
    [Test]
    public async Task PresentationKeepsRawTimeAndOnlyTransportChangesRevision()
    {
        var audio = new StubWorkstationAudioService();
        await using var coordinator = new MarkerPlaybackCoordinator(audio, (_, token) => Task.Delay(Timeout.Infinite, token));
        await coordinator.ConfigureAsync(new PlaybackMarkerClock(TimingTestData.Points([0], [120])), TimeSpan.FromSeconds(10), default);
        var revision = coordinator.Presentation.Revision;
        await coordinator.PlayAsync(default); Assert.That(coordinator.Presentation.Revision, Is.GreaterThan(revision));
        revision = coordinator.Presentation.Revision;
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(2010) };
        await coordinator.UpdateSamplesAsync("project.json", [], default);
        Assert.That(coordinator.Presentation.Revision, Is.EqualTo(revision));
        Assert.That(coordinator.Presentation.Playback.Position, Is.EqualTo(TimeSpan.FromMilliseconds(2010)));
        Assert.That(coordinator.Latest.Playback.Position, Is.Not.EqualTo(coordinator.Presentation.Playback.Position));
        await coordinator.SeekAsync(TimeSpan.FromSeconds(1), default); Assert.That(coordinator.Presentation.Revision, Is.GreaterThan(revision));
        revision = coordinator.Presentation.Revision;
        await coordinator.PauseAsync(default); Assert.That(coordinator.Presentation.Revision, Is.GreaterThan(revision));
        Assert.That(coordinator.Presentation.Playback.IsPlaying, Is.False);
        revision = coordinator.Presentation.Revision;
        await coordinator.StopAsync(default); Assert.That(coordinator.Presentation.Revision, Is.GreaterThan(revision));
        Assert.That(coordinator.Presentation.Playback.Position, Is.EqualTo(TimeSpan.Zero));
    }
    [Test]
    public async Task NativeWaitHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var wait = MarkerPlaybackCoordinator.DelayAsync(TimeSpan.FromSeconds(30), cancellation.Token);
        cancellation.Cancel();
        await wait.WaitAsync(TimeSpan.FromSeconds(3));
        await MarkerPlaybackCoordinator.DelayAsync(TimeSpan.FromMilliseconds(1), default);
    }

    [Test]
    public async Task NativeWaitAlreadyCancelledReturnsNormally() =>
        await MarkerPlaybackCoordinator.DelayAsync(TimeSpan.FromSeconds(30), new CancellationToken(true))
            .WaitAsync(TimeSpan.FromSeconds(3));

    [Test]
    public async Task NativeWaitSupportsRepeatedPauseAndStop()
    {
        var audio = new StubWorkstationAudioService();
        await using var coordinator = new MarkerPlaybackCoordinator(audio, MarkerPlaybackCoordinator.DelayAsync);
        await coordinator.ConfigureAsync(new PlaybackMarkerClock(TimingTestData.Points([0], [60])), TimeSpan.FromSeconds(10), default);
        for (var index = 0; index < 10; index++)
        {
            await coordinator.PlayAsync(default);
            await coordinator.PauseAsync(default).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(coordinator.Latest.Playback.IsPlaying, Is.False);
            await coordinator.PlayAsync(default);
            await coordinator.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(coordinator.Latest.Playback.Position, Is.EqualTo(TimeSpan.Zero));
        }
        Assert.That(coordinator.Latest.Playback.Error, Is.Null);
    }

    [Test]
    public async Task ReplacementGenerationResetsMarker()
    {
        var audio = new StubWorkstationAudioService();
        await using var coordinator = new MarkerPlaybackCoordinator(audio, (_, token) => Task.Delay(Timeout.Infinite, token));
        await coordinator.ConfigureAsync(new PlaybackMarkerClock(TimingTestData.Points([0], [120])), TimeSpan.FromSeconds(10), default);
        await coordinator.PlayAsync(default);
        var previous = coordinator.Latest.Generation;
        await coordinator.StopAsync(default);
        await coordinator.ConfigureAsync(new PlaybackMarkerClock(TimingTestData.Points([0], [240])), TimeSpan.FromSeconds(10), default);
        await coordinator.PlayAsync(default);
        Assert.That(coordinator.Latest.Generation, Is.GreaterThan(previous));
        Assert.That(coordinator.Latest.Playback.Position, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public async Task StopWaitsForInFlightSnapshotAndDisposedWorkerCannotRestart()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audio = new StubWorkstationAudioService();
        using var wake = new SemaphoreSlim(0);
        using var waiting = new SemaphoreSlim(0);
        await using var coordinator = new MarkerPlaybackCoordinator(audio, async (_, token) =>
        {
            waiting.Release();
            await wake.WaitAsync(token);
        });
        await coordinator.ConfigureAsync(new PlaybackMarkerClock(TimingTestData.Points([0], [120])), TimeSpan.FromSeconds(10), default);
        await coordinator.PlayAsync(default);
        await waiting.WaitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        audio.ReadingSnapshot = () => { entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException(); };
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(30) };
        wake.Release();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var stop = coordinator.StopAsync(default);
        try { Assert.That(stop.IsCompleted, Is.False); }
        finally { release.Set(); await stop; }
        Assert.That(coordinator.Latest.Playback.Position, Is.EqualTo(TimeSpan.Zero));
        Assert.That(coordinator.Latest.Playback.IsPlaying, Is.False);
        await coordinator.DisposeAsync();
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await coordinator.PlayAsync(default));
    }

    [Test]
    public async Task InitialPlayFailurePausesTransportAndCanRetry()
    {
        var audio = new StubWorkstationAudioService { Playing = () => throw new IOException("play failed") };
        await using var coordinator = new MarkerPlaybackCoordinator(audio, (_, token) => Task.Delay(Timeout.Infinite, token));
        await coordinator.ConfigureAsync(new PlaybackMarkerClock(TimingTestData.Points([0], [120])), TimeSpan.FromSeconds(10), default);
        Assert.ThrowsAsync<IOException>(async () => await coordinator.PlayAsync(default));
        Assert.That(audio.Snapshot.IsPlaying, Is.False);
        Assert.That(coordinator.Latest.Playback.Error, Is.EqualTo("play failed"));
        audio.Playing = null;
        await coordinator.PlayAsync(default);
        Assert.That(coordinator.Latest.Playback.IsPlaying, Is.True);
        Assert.That(coordinator.Latest.Playback.Error, Is.Null);
    }

    [Test]
    public void BoundaryWaitNeverBecomesASynchronousSpin() =>
        Assert.That(new PlaybackMarkerClock(TimingTestData.Points([0], [240])).GetDelayToNextBoundary(TimeSpan.FromMilliseconds(2)),
            Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1)));

    [Test]
    public async Task WorkerUpdatesMarkerWithoutPresentationAndStopsCleanly()
    {
        var audio = new StubWorkstationAudioService();
        using var wake = new SemaphoreSlim(0);
        using var stepped = new SemaphoreSlim(0);
        await using var coordinator = new MarkerPlaybackCoordinator(audio, async (_, token) =>
        {
            stepped.Release();
            await wake.WaitAsync(token);
        });
        await coordinator.ConfigureAsync(new PlaybackMarkerClock(TimingTestData.Points([0], [120])), TimeSpan.FromSeconds(10), default);
        await coordinator.PlayAsync(default);
        await stepped.WaitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(30) };
        wake.Release();
        await stepped.WaitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(coordinator.Latest.Playback.Position, Is.EqualTo(TimeSpan.FromMilliseconds(29.1667)));
        wake.Release();
        await stepped.WaitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(coordinator.Latest.Playback.Position, Is.EqualTo(TimeSpan.FromMilliseconds(29.1667)));
        await coordinator.StopAsync(default);
        Assert.That(coordinator.Latest.Playback.Position, Is.EqualTo(TimeSpan.Zero));
        Assert.That(coordinator.Latest.Playback.IsPlaying, Is.False);
    }

    [Test]
    public async Task PausePreservesBoundaryAndSeekResetsIt()
    {
        var audio = new StubWorkstationAudioService();
        await using var coordinator = new MarkerPlaybackCoordinator(audio, (_, token) => Task.Delay(Timeout.Infinite, token));
        await coordinator.ConfigureAsync(new PlaybackMarkerClock(TimingTestData.Points([0], [120])), TimeSpan.FromSeconds(10), default);
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(30) };
        await coordinator.PlayAsync(default);
        await coordinator.PauseAsync(default);
        Assert.That(coordinator.Latest.Playback.Position, Is.EqualTo(TimeSpan.FromMilliseconds(30)));
        await coordinator.PlayAsync(default);
        await coordinator.SeekAsync(TimeSpan.Zero, default);
        Assert.That(coordinator.Latest.Playback.Position, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void DelayHonorsTimingTransitionBeforeNextBoundary()
    {
        var clock = new PlaybackMarkerClock(TimingTestData.Points([0, 7], [120, 240]));
        Assert.That(clock.GetDelayToNextBoundary(TimeSpan.FromMilliseconds(6)), Is.EqualTo(TimeSpan.FromMilliseconds(1)));
        Assert.That(clock.GetPosition(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1)), Is.EqualTo(TimeSpan.FromTicks(90833)));
    }
}
