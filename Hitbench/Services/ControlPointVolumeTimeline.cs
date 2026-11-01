using Hitbench.Core.Entities;
namespace Hitbench.Services;

public sealed class ControlPointVolumeTimeline
{
    public static ControlPointVolumeTimeline Empty { get; } = new([]);
    private readonly long[] frames;
    public IReadOnlyList<ControlPointVolumeSegment> Segments { get; }
    public ControlPointVolumeTimeline(IReadOnlyList<ControlPointVolumeSnapshot> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var copy = points.ToArray();
        if (copy.Any(p => p is null || p.Volume is < 0 or > 100))
            throw new ArgumentException("Control point volumes must be 0–100.", nameof(points));
        var sorted = copy.OrderBy(p => p.Timestamp).GroupBy(p => p.Timestamp).Select(g => g.Last()).Where(p => !p.IsDisabled).ToArray();
        frames = sorted.Select(p => ToFrame(p.Timestamp)).ToArray();
        Segments = Array.AsReadOnly(sorted.Select((p, i) =>
        {
            var next = i + 1 < sorted.Length ? sorted[i + 1] : null;
            return new ControlPointVolumeSegment(p.Timestamp, next?.Timestamp, frames[i],
                next is null ? long.MaxValue : frames[i + 1], p.Volume,
                p.IsRamp && next is not null ? next.Volume : p.Volume, p.IsRamp && next is not null);
        }).ToArray());
    }
    public static long ToFrame(decimal milliseconds) => checked((long)Math.Round(milliseconds * 44100m / 1000m, MidpointRounding.AwayFromZero));
    public int FindSegment(long frame)
    {
        var index = Array.BinarySearch(frames, frame);
        return index < 0 ? ~index - 1 : index;
    }
    public float GetGain(long frame)
    {
        var index = FindSegment(frame);
        return index < 0 ? 1 : (float)Segments[index].EvaluateGain(frame);
    }
    public long GetNextBoundary(long frame)
    {
        var next = FindSegment(frame) + 1;
        return next < frames.Length ? frames[next] : long.MaxValue;
    }
    public decimal GetVolumeAtTimestamp(decimal milliseconds)
    {
        var low = 0; var high = Segments.Count;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (Segments[mid].StartTimestamp <= milliseconds) low = mid + 1; else high = mid;
        }
        if (low == 0) return 100;
        var segment = Segments[low - 1];
        return !segment.IsRamp || segment.EndTimestamp is not { } end ? segment.StartVolume
            : segment.StartVolume + (segment.EndVolume - segment.StartVolume) * Math.Clamp(
                (milliseconds - segment.StartTimestamp) / (end - (decimal)segment.StartTimestamp), 0, 1);
    }
}
