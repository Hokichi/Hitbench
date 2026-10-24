using Hitbench.Core.Entities;

namespace Hitbench.Services;

public sealed record NodeHistoryEntry(Track Track, Sample Sample, int Index, NodeHistoryOperation Operation,
    int PreviousTimestamp = 0, int NextTimestamp = 0,
    Track? DestinationTrack = null, int DestinationIndex = -1, string? PreviousFileName = null, string? NextFileName = null);
