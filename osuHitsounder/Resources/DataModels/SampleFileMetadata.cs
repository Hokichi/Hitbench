namespace osuHitsounder.Resources.DataModels;

public sealed record SampleFileMetadata(string FullPath, string Name, string Format, long Length,
    TimeSpan Duration, int SampleRate, int Channels);
