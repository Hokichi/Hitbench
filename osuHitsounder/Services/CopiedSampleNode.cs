using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services;

public sealed record CopiedSampleNode(Track Track, string FileName, long RelativeTimestamp);
