using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services;

public static class BeatGridCalculator
{
    private static readonly HashSet<int> Supported = [1, 2, 3, 4, 5, 6, 8, 12, 16];

    internal static (int Offset, int Bpm)[]? PrepareTiming(IReadOnlyList<int> offsets, IReadOnlyList<int> bpms)
    {
        if (offsets.Count == 0 || offsets.Count != bpms.Count || bpms.Any(bpm => bpm <= 0)) return null;
        return offsets.Select((offset, index) => (Offset: offset, Bpm: bpms[index], Index: index))
            .OrderBy(item => item.Offset).ThenBy(item => item.Index)
            .GroupBy(item => item.Offset).Select(group => (group.Last().Offset, group.Last().Bpm)).ToArray();
    }

    public static int? GetNearestTimestamp(IReadOnlyList<int> offsets, IReadOnlyList<int> bpms,
        int subdivision, double requestedMilliseconds, double durationMilliseconds,
        int minimumTimestamp = 0, int maximumTimestamp = int.MaxValue)
    {
        var segments = PrepareTiming(offsets, bpms);
        return segments is null ? null : GetNearestTimestamp(segments.AsSpan(), subdivision, requestedMilliseconds, durationMilliseconds, minimumTimestamp, maximumTimestamp);
    }

    internal static int? GetNearestTimestamp(ReadOnlySpan<(int Offset, int Bpm)> segments,
        int subdivision, double requestedMilliseconds, double durationMilliseconds,
        int minimumTimestamp = 0, int maximumTimestamp = int.MaxValue)
    {
        if (segments.Length == 0 || !Supported.Contains(subdivision) || !double.IsFinite(requestedMilliseconds) ||
            !double.IsFinite(durationMilliseconds) || durationMilliseconds <= 0 ||
            minimumTimestamp < 0 || minimumTimestamp > maximumTimestamp) return null;
        var maximum = Math.Min(maximumTimestamp, Math.Ceiling(durationMilliseconds) - 1);
        if (minimumTimestamp > maximum) return null;
        var target = Math.Clamp(requestedMilliseconds, minimumTimestamp, maximum);
        double bestDistance = double.PositiveInfinity, bestBeat = double.PositiveInfinity;
        int? result = null;
        Span<double> ticks = stackalloc double[9];
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var step = 60000d / segment.Bpm / subdivision;
            var end = index + 1 < segments.Length ? Math.Min(durationMilliseconds, segments[index + 1].Offset) : durationMilliseconds;
            var lower = Math.Max(0, minimumTimestamp - 0.5);
            var upper = Math.Min(end, maximum + 0.5);
            if (upper < lower || end <= segment.Offset) continue;
            var first = Math.Max(0, Math.Floor((lower - segment.Offset) / step) - 1);
            var last = Math.Floor((upper - segment.Offset) / step) + 1;
            if (last < first) continue;
            var nearest = Math.Clamp(Math.Floor((target - segment.Offset) / step), first, last);
            ticks[0] = first; ticks[1] = first + 1; ticks[2] = first + 2;
            ticks[3] = last; ticks[4] = last - 1; ticks[5] = last - 2;
            ticks[6] = nearest - 1; ticks[7] = nearest; ticks[8] = nearest + 1;
            foreach (var tick in ticks)
            {
                if (tick < 0) continue;
                var beat = segment.Offset + tick * step;
                var timestamp = Math.Round(beat, MidpointRounding.AwayFromZero);
                if (!double.IsFinite(beat) || beat < 0 || beat >= end || timestamp < minimumTimestamp || timestamp > maximum) continue;
                var distance = Math.Abs(beat - target);
                if (distance > bestDistance + 1e-9 || Math.Abs(distance - bestDistance) <= 1e-9 && beat >= bestBeat) continue;
                bestDistance = distance;
                bestBeat = beat;
                result = (int)timestamp;
            }
        }
        return result;
    }

    public static BeatInterval? GetContainingInterval(IReadOnlyList<int> offsets, IReadOnlyList<int> bpms,
        int subdivision, double positionMilliseconds, double durationMilliseconds)
    {
        if (offsets.Count == 0 || offsets.Count != bpms.Count || !Supported.Contains(subdivision) ||
            bpms.Any(bpm => bpm <= 0) || !double.IsFinite(positionMilliseconds) ||
            !double.IsFinite(durationMilliseconds) || durationMilliseconds <= 0 ||
            positionMilliseconds < 0 || positionMilliseconds >= durationMilliseconds) return null;

        var segments = offsets.Select((offset, index) => (Offset: offset, Bpm: bpms[index], Index: index))
            .OrderBy(item => item.Offset).ThenBy(item => item.Index)
            .GroupBy(item => item.Offset).Select(group => group.Last()).ToArray();
        var segmentIndex = Array.FindLastIndex(segments, segment => segment.Offset <= positionMilliseconds);
        if (segmentIndex < 0) return null;
        var segment = segments[segmentIndex];
        var interval = 60000d / segment.Bpm / subdivision;
        if (!double.IsFinite(interval) || interval <= 0) return null;
        var relative = (positionMilliseconds - segment.Offset) / interval;
        var nearest = Math.Round(relative);
        var tick = Math.Abs(relative - nearest) < 1e-9 ? nearest : Math.Floor(relative);
        var start = segment.Offset + tick * interval;
        var segmentEnd = segmentIndex + 1 < segments.Length
            ? Math.Min(durationMilliseconds, segments[segmentIndex + 1].Offset)
            : durationMilliseconds;
        var end = Math.Min(segment.Offset + (tick + 1) * interval, segmentEnd);
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start ||
            positionMilliseconds < start || positionMilliseconds >= end) return null;
        return new BeatInterval(start, end);
    }

    public static TimeSpan? GetAdjacentPosition(IReadOnlyList<int> offsets, IReadOnlyList<int> bpms,
        int subdivision, TimeSpan position, TimeSpan duration, bool forward)
    {
        if (offsets.Count == 0 || offsets.Count != bpms.Count || !Supported.Contains(subdivision) ||
            bpms.Any(bpm => bpm <= 0) || duration <= TimeSpan.Zero) return null;

        var segments = offsets.Select((offset, index) => (Offset: offset, Bpm: bpms[index], Index: index))
            .OrderBy(item => item.Offset).ThenBy(item => item.Index)
            .GroupBy(item => item.Offset).Select(group => group.Last()).ToArray();
        TimeSpan? best = null;
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var interval = 60000d / segment.Bpm / subdivision;
            var relative = (position.TotalMilliseconds - segment.Offset) / interval;
            var tick = forward ? Math.Max(0, Math.Floor(relative)) : Math.Ceiling(relative);
            if (interval < 1d / TimeSpan.TicksPerMillisecond)
            {
                var nextRepresentableMs = (position.Ticks + (forward ? 1d : -1d)) / TimeSpan.TicksPerMillisecond;
                var representableTick = (nextRepresentableMs - segment.Offset) / interval;
                tick = forward ? Math.Max(tick, Math.Ceiling(representableTick))
                    : Math.Min(tick, Math.Floor(representableTick));
            }
            if (!forward)
            {
                if (index + 1 < segments.Length)
                {
                    var nextOffset = segments[index + 1].Offset;
                    var lastTick = Math.Floor((nextOffset - segment.Offset) / interval);
                    for (var repair = 0; repair < 3 &&
                         segment.Offset + lastTick * interval >= nextOffset; repair++) lastTick--;
                    tick = Math.Min(tick, lastTick);
                }
                tick = Math.Min(tick, Math.Floor((duration.TotalMilliseconds - segment.Offset) / interval));
            }
            if (!forward && tick < 0) continue;
            for (var adjustment = 0; adjustment < 3 && tick >= 0; adjustment++, tick += forward ? 1 : -1)
            {
                var milliseconds = segment.Offset + tick * interval;
                if (!double.IsFinite(milliseconds) || milliseconds > duration.TotalMilliseconds ||
                    index + 1 < segments.Length && milliseconds >= segments[index + 1].Offset) break;
                if (milliseconds < 0)
                {
                    if (forward) continue;
                    break;
                }
                var candidate = TimeSpan.FromMilliseconds(milliseconds);
                if (forward ? candidate <= position : candidate >= position) continue;
                if (best is null || (forward ? candidate < best : candidate > best)) best = candidate;
                break;
            }
        }
        return best;
    }

    public static IReadOnlyList<BeatGridLine> GetLines(IReadOnlyList<int> offsets, IReadOnlyList<int> bpms,
        int subdivision, double visibleStartMs, double visibleEndMs)
    {
        if (offsets.Count == 0 || offsets.Count != bpms.Count || !Supported.Contains(subdivision) ||
            !double.IsFinite(visibleStartMs) || !double.IsFinite(visibleEndMs) || visibleEndMs < visibleStartMs ||
            bpms.Any(bpm => bpm <= 0)) return [];

        var segments = offsets.Select((offset, index) => (Offset: offset, Bpm: bpms[index], Index: index))
            .OrderBy(item => item.Offset).ThenBy(item => item.Index)
            .GroupBy(item => item.Offset).Select(group => group.Last()).ToArray();
        var result = new List<BeatGridLine>();
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var end = index + 1 < segments.Length ? Math.Min(visibleEndMs, segments[index + 1].Offset - 0.000001) : visibleEndMs;
            var start = Math.Max(visibleStartMs, segment.Offset);
            if (start > end) continue;
            var interval = 60000d / segment.Bpm / subdivision;
            if ((end - start) / interval > 100000)
                return subdivision == 1 ? [] : GetLines(offsets, bpms, 1, visibleStartMs, visibleEndMs);
            var firstTick = Math.Max(0L, (long)Math.Ceiling((start - segment.Offset) / interval - 1e-9));
            var lastTick = (long)Math.Floor((end - segment.Offset) / interval + 1e-9);
            for (var tick = firstTick; tick <= lastTick; tick++)
            {
                var time = segment.Offset + tick * interval;
                if (time < start - 1e-6 || time > end + 1e-6) continue;
                result.Add(new BeatGridLine(time, subdivision / GreatestCommonDivisor((int)(tick % subdivision), subdivision)));
            }
        }
        return result;
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0) (left, right) = (right, left % right);
        return left;
    }
}
