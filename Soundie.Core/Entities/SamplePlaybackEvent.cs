namespace Soundie.Core.Entities;

public sealed record SamplePlaybackEvent(
    long Id,
    string FileName,
    int Timestamp,
    int TrackVolume);
