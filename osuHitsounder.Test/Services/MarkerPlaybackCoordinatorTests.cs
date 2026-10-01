using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Test.Presentation;

namespace osuHitsounder.Test.Services;

public class MarkerPlaybackCoordinatorTests
{
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
