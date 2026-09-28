namespace osuHitsounder.Services;

public sealed record PreparedSampleEvent(
    long Id,
    ReadOnlyMemory<float> Samples,
    float Gain);
