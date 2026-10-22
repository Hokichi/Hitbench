using Soundie.Core.Entities;

namespace Soundie.Resources.DataModels;

public sealed record SongCache(int Version, string SongsRoot, string[] Folders, Song[] Songs);
