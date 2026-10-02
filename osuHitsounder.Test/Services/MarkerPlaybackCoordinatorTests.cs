using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Test.Presentation;

namespace osuHitsounder.Test.Services;

public class MarkerPlaybackCoordinatorTests
{
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
    public async Task NativeWaitAlreadyCancelledReturnsNormally()
    {
        await MarkerPlaybackCoordinator.DelayAsync(TimeSpan.FromSeconds(30), new CancellationToken(true))
            .WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task NativeWaitSupportsRepeatedPauseAndStopWithoutStaleTriggers()
    {
        var audio = new StubWorkstationAudioService();
        await using var coordinator = new MarkerPlaybackCoordinator(audio, MarkerPlaybackCoordinator.DelayAsync);
        await coordinator.ConfigureAsync(new PlaybackMarkerClock([0], [60]), TimeSpan.FromSeconds(10),
            [new(1, "hit", 5000, 100, 100)], default);
        for (var index = 0; index < 10; index++)
        {
            await coordinator.PlayAsync(default);
            await coordinator.PauseAsync(default).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(coordinator.Latest.Playback.IsPlaying, Is.False);
            await coordinator.PlayAsync(default);
            await coordinator.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(coordinator.Latest.Playback.Position, Is.EqualTo(TimeSpan.Zero));
        }
        Assert.That(audio.TriggeredSamples, Is.Empty);
        Assert.That(coordinator.Latest.Playback.Error, Is.Null);
    }

    [Test]
    public async Task ReplacementGenerationCannotReceiveOldWorkerTriggers()
    {
        var audio = new StubWorkstationAudioService();
        await using var coordinator = new MarkerPlaybackCoordinator(audio, (_, token) => Task.Delay(Timeout.Infinite, token));
        await coordinator.ConfigureAsync(new PlaybackMarkerClock([0], [120]), TimeSpan.FromSeconds(10), [new(1,"hit",10,100,100)], default);
        await coordinator.PlayAsync(default);
        var previous = coordinator.Latest.Generation;
        await coordinator.StopAsync(default);
        await coordinator.ConfigureAsync(new PlaybackMarkerClock([0], [240]), TimeSpan.FromSeconds(10), [new(2,"hit",0,100,100)], default);
        await coordinator.PlayAsync(default);
        Assert.That(coordinator.Latest.Generation, Is.GreaterThan(previous));
        Assert.That(audio.TriggeredSamples.SelectMany(x => x), Is.EqualTo(new long[] { 2 }));
    }
    [Test]
    public async Task StopWaitsForInFlightTriggerAndDisposedWorkerCannotRestart()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audio = new StubWorkstationAudioService();
        var wake = new SemaphoreSlim(0);
        var coordinator = new MarkerPlaybackCoordinator(audio, (_, token) => wake.WaitAsync(token));
        await coordinator.ConfigureAsync(new PlaybackMarkerClock([0], [120]), TimeSpan.FromSeconds(10), [new(1,"hit",10,100,100)], default);
        await coordinator.PlayAsync(default);
        audio.Triggering = _ => { entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException(); };
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(30) };
        wake.Release();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var stop = coordinator.StopAsync(default);
        Assert.That(stop.IsCompleted, Is.False);
        release.Set();
        await stop;
        Assert.That(coordinator.Latest.Playback.Position, Is.EqualTo(TimeSpan.Zero));
        Assert.That(audio.TriggeredSamples.SelectMany(x => x), Is.EqualTo(new long[] { 1 }));
        await coordinator.DisposeAsync();
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await coordinator.PlayAsync(default));
    }

    [Test]
    public async Task InitialTriggerFailurePausesTransportAndCanRetry()
    {
        var audio = new StubWorkstationAudioService { Triggering = _ => throw new IOException("trigger failed") };
        await using var coordinator = new MarkerPlaybackCoordinator(audio, (_, token) => Task.Delay(Timeout.Infinite, token));
        await coordinator.ConfigureAsync(new PlaybackMarkerClock([0], [120]), TimeSpan.FromSeconds(10), [new(1,"hit",0,100,100)], default);
        Assert.ThrowsAsync<IOException>(async () => await coordinator.PlayAsync(default));
        Assert.That(audio.Snapshot.IsPlaying, Is.False);
        Assert.That(coordinator.Latest.Playback.Error, Is.EqualTo("trigger failed"));
        audio.Triggering = null;
        await coordinator.PlayAsync(default);
        Assert.That(audio.TriggeredSamples.SelectMany(x => x), Is.EqualTo(new long[] { 1 }));
    }

    [Test]
    public void BoundaryWaitNeverBecomesASynchronousSpin()
    {
        Assert.That(new PlaybackMarkerClock([0], [240]).GetDelayToNextBoundary(TimeSpan.FromMilliseconds(2)),
            Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1)));
    }
    [Test]
    public async Task WorkerCrossesSamplesWithoutPresentationAndStopsCleanly()
    {
        var audio = new StubWorkstationAudioService();
        var wake = new SemaphoreSlim(0);
        var stepped = new SemaphoreSlim(0);
        await using var coordinator = new MarkerPlaybackCoordinator(audio, async (_, token) =>
        {
            stepped.Release();
            await wake.WaitAsync(token);
        });
        await coordinator.ConfigureAsync(new PlaybackMarkerClock([0], [120]), TimeSpan.FromSeconds(10),
            [new(1, "hit", 0, 100, 100), new(2, "hit", 10, 100, 100), new(3, "hit", 10, 100, 100)], default);
        await coordinator.PlayAsync(default);
        Assert.That(await stepped.WaitAsync(TimeSpan.FromSeconds(3)), Is.True);
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(30) };
        wake.Release();
        Assert.That(await stepped.WaitAsync(TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(audio.TriggeredSamples.SelectMany(x => x), Is.EqualTo(new long[] { 1, 2, 3 }));
        wake.Release();
        Assert.That(await stepped.WaitAsync(TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(audio.TriggeredSamples.SelectMany(x => x).Count(), Is.EqualTo(3));
        await coordinator.StopAsync(default);
        Assert.That(coordinator.Latest.Playback.Position, Is.EqualTo(TimeSpan.Zero));
        Assert.That(coordinator.Latest.Playback.IsPlaying, Is.False);
    }

    [Test]
    public async Task PausePreservesBoundaryAndSeekResetsIt()
    {
        var audio = new StubWorkstationAudioService();
        await using var coordinator = new MarkerPlaybackCoordinator(audio, (_, token) => Task.Delay(Timeout.Infinite, token));
        await coordinator.ConfigureAsync(new PlaybackMarkerClock([0], [120]), TimeSpan.FromSeconds(10), [new(1,"hit",0,100,100)], default);
        await coordinator.PlayAsync(default);
        await coordinator.PauseAsync(default);
        await coordinator.PlayAsync(default);
        Assert.That(audio.TriggeredSamples.SelectMany(x => x), Is.EqualTo(new long[] { 1 }));
        await coordinator.SeekAsync(TimeSpan.Zero, default);
        Assert.That(audio.TriggeredSamples.SelectMany(x => x), Is.EqualTo(new long[] { 1, 1 }));
    }

    [Test]
    public void DelayHonorsTimingTransitionBeforeNextBoundary()
    {
        var clock = new PlaybackMarkerClock([0, 7], [120, 240]);
        Assert.That(clock.GetDelayToNextBoundary(TimeSpan.FromMilliseconds(6)), Is.EqualTo(TimeSpan.FromMilliseconds(1)));
        Assert.That(clock.GetPosition(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1)), Is.EqualTo(TimeSpan.FromTicks(90833)));
    }
}
