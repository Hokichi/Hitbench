namespace osuHitsounder.Core.Entities;

public sealed record SamplePlaybackEvent(
    long Id,
    string FileName,
    int Timestamp,
    int BeatVolume,
    int TrackVolume);
