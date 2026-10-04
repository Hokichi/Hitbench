using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services;

public sealed record NodeHistoryEntry(Track Track, Sample Sample, int Index, NodeHistoryOperation Operation,
    int PreviousTimestamp = 0, int NextTimestamp = 0);
