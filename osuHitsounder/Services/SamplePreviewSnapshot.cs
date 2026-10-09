namespace osuHitsounder.Services;

public sealed record SamplePreviewSnapshot(bool IsPlaying, string? Error, string? Warning,
    TimeSpan Position = default, TimeSpan Duration = default);
