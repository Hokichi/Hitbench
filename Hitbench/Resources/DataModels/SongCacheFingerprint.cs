namespace Hitbench.Resources.DataModels;
public sealed record SongCacheFingerprint(string BeatmapName, long Length, DateTime LastWriteTimeUtc, string? FallbackImageName, bool BackgroundExists);
