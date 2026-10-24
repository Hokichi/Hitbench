using Hitbench.Core.Entities;

namespace Hitbench.Test.Services;

public static class MarkerStressFixture
{
    public static SamplePlaybackEvent[] Create(int tracks, int placements = 1000, int seed = 930)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, tracks).SelectMany(track => Enumerable.Range(0, placements)
            .Select(index => new SamplePlaybackEvent(track * (long)placements + index + 1, "hit.wav",
                index % 10 == 0 ? index * 300 : Math.Min(299999, index * 300 + random.Next(0, 40)), 100))).ToArray();
    }
}
