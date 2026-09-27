namespace osuHitsounder.Services;

public sealed record PreparedSampleEvent(
    long Id,
    long StartFrame,
    ReadOnlyMemory<float> Samples,
    float Gain);
