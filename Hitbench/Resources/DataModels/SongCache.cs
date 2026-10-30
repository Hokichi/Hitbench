using Hitbench.Core.Entities;

namespace Hitbench.Resources.DataModels;

public sealed record SongCache(int Version, string SongsRoot, string[] Folders, Song[] Songs, Dictionary<string, SongCacheFingerprint>? Fingerprints = null);
