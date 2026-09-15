using osuHitsounder.Core.Entities;

namespace osuHitsounder.Resources.DataModels;

public sealed record SongCache(int Version, string SongsRoot, string[] Folders, Song[] Songs);
