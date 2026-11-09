using Hitbench.Services;

namespace Hitbench.Test.Services;

public class PlaybackPresentationClockTests
{
    private static PlaybackPresentationSnapshot Snapshot(double ms, long revision = 1, bool playing = true) => new(revision, new(TimeSpan.FromMilliseconds(ms), playing, null));
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(10);
    [Test]
    public void WaitsForProgressThenPredictsFractionallyWithBoundedStaleHorizon()
    {
        var clock = new PlaybackPresentationClock();
        Assert.That(clock.GetPosition(Snapshot(2000), TimeSpan.Zero, Duration).TotalMilliseconds, Is.EqualTo(2000));
        Assert.That(clock.GetPosition(Snapshot(2000), TimeSpan.FromMilliseconds(8), Duration).TotalMilliseconds, Is.EqualTo(2000));
        clock.GetPosition(Snapshot(2010), TimeSpan.FromMilliseconds(10), Duration);
        Assert.That(clock.GetPosition(Snapshot(2010), TimeSpan.FromMilliseconds(26), Duration).TotalMilliseconds, Is.EqualTo(2026));
        Assert.That(clock.GetPosition(Snapshot(2010), TimeSpan.FromSeconds(1), Duration).TotalMilliseconds, Is.EqualTo(2110));
        Assert.That(clock.GetPosition(Snapshot(2010), TimeSpan.FromSeconds(2), Duration).TotalMilliseconds, Is.EqualTo(2110));
    }
    [Test]
    public void RevisionsPauseResetAndBackwardsFramesReanchorExactly()
    {
        var clock = new PlaybackPresentationClock();
        clock.GetPosition(Snapshot(2000), TimeSpan.Zero, Duration);
        clock.GetPosition(Snapshot(2010), TimeSpan.FromMilliseconds(10), Duration);
        Assert.That(clock.GetPosition(Snapshot(1000, 2), TimeSpan.FromMilliseconds(26), Duration).TotalMilliseconds, Is.EqualTo(1000));
        Assert.That(clock.GetPosition(Snapshot(900, 2, false), TimeSpan.FromMilliseconds(30), Duration).TotalMilliseconds, Is.EqualTo(900));
        clock.Reset();
        Assert.That(clock.GetPosition(Snapshot(800), TimeSpan.FromMilliseconds(20), Duration).TotalMilliseconds, Is.EqualTo(800));
        Assert.That(clock.GetPosition(Snapshot(700), TimeSpan.Zero, Duration).TotalMilliseconds, Is.EqualTo(700));
        Assert.That(clock.GetPosition(Snapshot(20000), TimeSpan.Zero, Duration), Is.EqualTo(Duration));
        Assert.That(clock.GetPosition(Snapshot(20000), TimeSpan.Zero, TimeSpan.Zero), Is.EqualTo(TimeSpan.Zero));
    }
    [Test]
    public void CoarseObservationsDoNotMovePredictedPositionBackwards()
    {
        var clock = new PlaybackPresentationClock();
        clock.GetPosition(Snapshot(2000), TimeSpan.Zero, Duration);
        clock.GetPosition(Snapshot(2010), TimeSpan.FromMilliseconds(10), Duration);
        var predicted = clock.GetPosition(Snapshot(2010), TimeSpan.FromMilliseconds(26), Duration);
        Assert.That(clock.GetPosition(Snapshot(2020), TimeSpan.FromMilliseconds(27), Duration), Is.GreaterThanOrEqualTo(predicted));
        Assert.That(clock.GetPosition(Snapshot(2010), TimeSpan.FromMilliseconds(28), Duration), Is.GreaterThanOrEqualTo(predicted));
    }
}
