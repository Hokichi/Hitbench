using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services;

public sealed class MarkerTraversal
{
    private SamplePlaybackEvent[] events = [];
    private TimeSpan processed;
    private bool includeStart = true;
    private int cursor;

    public void Replace(IReadOnlyList<SamplePlaybackEvent> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var copy = value.ToArray();
        var ids = new HashSet<long>();
        if (copy.Any(e => e is null || e.Id <= 0 || !ids.Add(e.Id)))
            throw new ArgumentException("Placement identities must be positive and unique.", nameof(value));
        Array.Sort(copy, (a, b) => a.Timestamp != b.Timestamp ? a.Timestamp.CompareTo(b.Timestamp) : a.Id.CompareTo(b.Id));
        events = copy;
        Reset(processed, includeStart);
    }

    public void Reset(TimeSpan position, bool includeStart)
    {
        processed = position;
        this.includeStart = includeStart;
        var low = 0;
        var high = events.Length;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            var ticks = events[mid].Timestamp * TimeSpan.TicksPerMillisecond;
            if (ticks < position.Ticks || !includeStart && ticks == position.Ticks) low = mid + 1;
            else high = mid;
        }
        cursor = low;
    }

    public void Advance(TimeSpan marker, List<long> destination)
    {
        destination.Clear();
        if (marker < processed) return;
        while (cursor < events.Length && events[cursor].Timestamp * TimeSpan.TicksPerMillisecond <= marker.Ticks)
            destination.Add(events[cursor++].Id);
        processed = marker;
        includeStart = false;
    }
}
