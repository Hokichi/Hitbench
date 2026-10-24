using Hitbench.Core.Entities;

namespace Hitbench.Services;

public sealed class ControlPointVolumeTimeline
{
    public static ControlPointVolumeTimeline Empty { get; } = new([]);
    private readonly long[] frames;
    private readonly float[] gains;

    public ControlPointVolumeTimeline(IReadOnlyList<ControlPointVolumeSnapshot> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var copy = points.ToArray();
        if (copy.Any(p => p is null || p.Timestamp < 0 || p.Volume is < 0 or > 100))
            throw new ArgumentException("Control points require nonnegative timestamps and volumes 0–100.", nameof(points));
        var sorted = copy.OrderBy(p => p.Timestamp).GroupBy(p => p.Timestamp).Select(g => g.Last()).ToArray();
        frames = sorted.Select(p => (long)Math.Round(p.Timestamp * 44100m / 1000m, MidpointRounding.AwayFromZero)).ToArray();
        gains = new float[sorted.Length];
        var effective = 1f;
        for (var index = 0; index < sorted.Length; index++)
        {
            if (!sorted[index].IsDisabled) effective = sorted[index].Volume / 100f;
            gains[index] = effective;
        }
    }

    public float GetGain(long frame)
    {
        var index = Array.BinarySearch(frames, frame);
        if (index < 0) index = ~index - 1;
        return index < 0 ? 1 : gains[index];
    }

    public long GetNextBoundary(long frame)
    {
        var index = Array.BinarySearch(frames, frame);
        index = index < 0 ? ~index : index + 1;
        return index < frames.Length ? frames[index] : long.MaxValue;
    }
}
