namespace osuHitsounder.Test.Presentation;

// Fixture input retained for the existing project-creation integration scenarios.
public sealed record HomeFolderData(
    IReadOnlyList<DirectoryInfo> Projects, IReadOnlyList<DirectoryInfo> Songs,
    string? OsuDirectory, bool SongsFolderExists, bool SkippedLocations);
