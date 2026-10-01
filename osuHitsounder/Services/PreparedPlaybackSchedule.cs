namespace osuHitsounder.Services;

public sealed class PreparedPlaybackSchedule
{
    public static PreparedPlaybackSchedule Empty { get; } = new([]);
    private readonly PreparedSampleEvent[] events;
    private readonly Dictionary<long, PreparedSampleEvent> byId;
    public bool TryGetEvent(long id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PreparedSampleEvent? placement) =>
        byId.TryGetValue(id, out placement);
    public ReadOnlySpan<PreparedSampleEvent> Events => events;

    public PreparedPlaybackSchedule(IReadOnlyList<PreparedSampleEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var ids = new HashSet<long>();
        this.events = events.ToArray();
        foreach (var item in this.events)
        {
            if (item is null || item.Audio is null || item.Id <= 0 || !ids.Add(item.Id) ||
                !float.IsFinite(item.Gain) || item.Gain < 0 || item.Gain > 1)
                throw new ArgumentException("Invalid or duplicate sample placement.", nameof(events));
            _ = item.EndFrame;
        }
        Array.Sort(this.events, (a, b) => a.StartFrame != b.StartFrame
            ? a.StartFrame.CompareTo(b.StartFrame) : a.Id.CompareTo(b.Id));
        byId = this.events.ToDictionary(item => item.Id);
    }
}
