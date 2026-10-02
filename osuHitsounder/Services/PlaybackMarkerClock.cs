namespace osuHitsounder.Services;

/// <summary>Maps song time onto the timing map's 1/120-beat boundaries.</summary>
public sealed class PlaybackMarkerClock
{
    private readonly (int Offset, int Bpm)[] segments;

    public PlaybackMarkerClock(IReadOnlyList<int> offsets, IReadOnlyList<int> bpms)
    {
        IsValid = offsets.Count > 0 && offsets.Count == bpms.Count && bpms.All(bpm => bpm > 0);
        segments = IsValid
            ? offsets.Select((offset, index) => (Offset: offset, Bpm: bpms[index]))
                .OrderBy(segment => segment.Offset).GroupBy(segment => segment.Offset)
                .Select(group => group.Last()).ToArray()
            : [];
    }

    public bool IsValid { get; }

    public TimeSpan GetDelayToNextBoundary(TimeSpan position)
    {
        var segment = GetSegment(position);
        var origin = (long)segment.Offset * TimeSpan.TicksPerMillisecond;
        var interval = IntervalTicks(segment.Bpm);
        var index = Math.Floor((position.Ticks - origin) / interval) + 1;
        var next = origin + (long)Math.Round(index * interval);
        if (next <= position.Ticks) next = origin + (long)Math.Round((index + 1) * interval);
        foreach (var candidate in segments)
            if (candidate.Offset * TimeSpan.TicksPerMillisecond > position.Ticks)
            {
                next = Math.Min(next, candidate.Offset * TimeSpan.TicksPerMillisecond);
                break;
            }
        // Task.Delay truncates sub-millisecond waits to zero; never turn a boundary wait into a spin.
        return TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerMillisecond, next - position.Ticks));
    }

    public TimeSpan GetInterval(TimeSpan position)
    {
        var segment = GetSegment(position);
        return TimeSpan.FromTicks(Math.Max(1, (long)Math.Round(IntervalTicks(segment.Bpm))));
    }

    public TimeSpan GetPosition(TimeSpan songPosition, TimeSpan duration)
    {
        var segment = GetSegment(songPosition);
        if (songPosition <= TimeSpan.Zero || duration <= TimeSpan.Zero) return TimeSpan.Zero;
        if (songPosition >= duration) return duration;
        var origin = (long)segment.Offset * TimeSpan.TicksPerMillisecond;
        var interval = IntervalTicks(segment.Bpm);
        var relative = (songPosition.Ticks - origin) / interval;
        // Round each absolute boundary once, rather than accumulating a rounded interval.
        var index = Math.Floor(relative);
        var next = origin + Math.Round((index + 1) * interval);
        if (next <= songPosition.Ticks) index++;
        var boundary = origin + Math.Round(index * interval);
        return TimeSpan.FromTicks(Math.Clamp((long)boundary, 0, songPosition.Ticks));
    }

    private (int Offset, int Bpm) GetSegment(TimeSpan position)
    {
        if (!IsValid) throw new InvalidOperationException("Playback requires valid BPM timing data.");
        var index = Array.FindLastIndex(segments, segment => segment.Offset <= position.TotalMilliseconds);
        return segments[Math.Max(0, index)];
    }

    private static double IntervalTicks(int bpm) => TimeSpan.TicksPerMinute / (double)bpm / 120;
}
