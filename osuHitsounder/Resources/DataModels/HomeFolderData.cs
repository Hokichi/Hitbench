using System.IO;

namespace osuHitsounder.Resources.DataModels;

public sealed record HomeFolderData(
    IReadOnlyList<DirectoryInfo> Projects,
    IReadOnlyList<DirectoryInfo> Songs,
    string? OsuDirectory,
    bool SongsFolderExists,
    bool SkippedLocations);
