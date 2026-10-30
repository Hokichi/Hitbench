namespace Hitbench.Test.Presentation;

public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset now;
    private long timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => timestamp;
    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        timestamp += elapsed.Ticks;
        now += elapsed;
    }
    public void SetUtcNow(DateTimeOffset value) => now = value;
    public override DateTimeOffset GetUtcNow() => now;
}
