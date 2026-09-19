namespace osuHitsounder.Core.Entities;

public sealed record AudioPlaybackSnapshot(TimeSpan Position, bool IsPlaying, string? Error);
