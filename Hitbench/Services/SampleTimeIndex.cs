using Hitbench.Core.Entities;

namespace Hitbench.Services;

/// <summary>Timestamp candidates retain the original collection's identity and paint order.</summary>
public sealed class SampleTimeIndex
{
    private IReadOnlyList<Sample>? source;
    private int revision, count;
    private (int Timestamp, int Position, Sample Sample)[] entries = [];

    public void Invalidate() { source = null; count = -1; }

    public void Update(IReadOnlyList<Sample>? samples, int revision)
    {
        if (ReferenceEquals(source, samples) && this.revision == revision && count == (samples?.Count ?? 0)) return;
        source = samples;
        this.revision = revision;
        count = samples?.Count ?? 0;
        entries = samples?.Select((sample, position) => (sample.Timestamp, position, sample))
            .OrderBy(item => item.Timestamp).ThenBy(item => item.position).ToArray() ?? [];
    }

    public IReadOnlyList<Sample> Query(double minimumTimestamp, double maximumTimestamp)
    {
        if (!double.IsFinite(minimumTimestamp) || !double.IsFinite(maximumTimestamp) || minimumTimestamp > maximumTimestamp) return [];
        var start = Bound(minimumTimestamp, false);
        var end = Bound(maximumTimestamp, true);
        if (start == end) return [];
        return entries.AsSpan(start, end - start).ToArray().OrderBy(item => item.Position).Select(item => item.Sample).ToArray();
    }

    private int Bound(double timestamp, bool upper)
    {
        var low = 0;
        var high = entries.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (entries[middle].Timestamp < timestamp || upper && entries[middle].Timestamp == timestamp) low = middle + 1;
            else high = middle;
        }
        return low;
    }
}
