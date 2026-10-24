using Onset.Core.Entities;

namespace Onset.Resources.DataModels;

public sealed record SongCache(int Version, string SongsRoot, string[] Folders, Song[] Songs);
