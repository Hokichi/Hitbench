using osuHitsounder.Core.Entities;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class MarkerTraversalTests
{
    private static SamplePlaybackEvent Event(long id, int time) => new(id, "hit.wav", time, 100, 100);

    [Test]
    public void CrossingReplacementAndSeekRespectProcessedBoundary()
    {
        var traversal = new MarkerTraversal();
        traversal.Replace([Event(1, 0), Event(2, 10), Event(3, 10), Event(4, 31)]);
        var ids = new List<long>();
        traversal.Reset(TimeSpan.Zero, true);
        traversal.Advance(TimeSpan.FromMilliseconds(12.5), ids);
        Assert.That(ids, Is.EqualTo(new long[] { 1, 2, 3 }));
        traversal.Advance(TimeSpan.FromMilliseconds(12.5), ids);
        Assert.That(ids, Is.Empty);
        traversal.Replace([Event(5, 1), Event(6, 20)]);
        traversal.Advance(TimeSpan.FromMilliseconds(40), ids);
        Assert.That(ids, Is.EqualTo(new long[] { 6 }));
        traversal.Reset(TimeSpan.FromMilliseconds(1), true);
        traversal.Advance(TimeSpan.FromMilliseconds(1), ids);
        Assert.That(ids, Is.EqualTo(new long[] { 5 }));
        traversal.Reset(TimeSpan.FromMilliseconds(1), false);
        traversal.Advance(TimeSpan.FromMilliseconds(1), ids);
        Assert.That(ids, Is.Empty);
    }

    [Test]
    public void InvalidReplacementDoesNotCorruptCurrentEvents()
    {
        var traversal = new MarkerTraversal();
        traversal.Replace([Event(1, 0)]);
        Assert.Throws<ArgumentException>(() => traversal.Replace([Event(2, 0), Event(2, 1)]));
        var ids = new List<long>();
        traversal.Advance(TimeSpan.Zero, ids);
        Assert.That(ids, Is.EqualTo(new long[] { 1 }));
    }

    [TestCase(64), TestCase(128)]
    public void FullSongCrossesEveryPlacementOnce(int tracks)
    {
        var events = MarkerStressFixture.Create(tracks);
        var traversal = new MarkerTraversal();
        traversal.Replace(events);
        var actual = new List<long>();
        var batch = new List<long>();
        for (var ms = 0; ms <= 300000; ms += 17)
        {
            traversal.Advance(TimeSpan.FromMilliseconds(ms), batch);
            actual.AddRange(batch);
        }
        traversal.Advance(TimeSpan.FromMinutes(5), batch);
        actual.AddRange(batch);
        Assert.That(actual, Is.EqualTo(events.OrderBy(e => e.Timestamp).ThenBy(e => e.Id).Select(e => e.Id)));
    }
}
