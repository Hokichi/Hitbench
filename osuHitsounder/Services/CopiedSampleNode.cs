using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services;

public sealed record CopiedSampleNode(Track Track, string FileName, int Volume, long RelativeTimestamp);
