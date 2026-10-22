using System.IO;

namespace Soundie.Resources.DataModels;

public sealed record FolderEntry(string FullName, string Name, DateTimeOffset LastWriteTimeUtc)
{
    public string DisplayName { get; init; } = Name;
    public string Artist { get; init; } = "";
    public string Title { get; init; } = "";
    public string BackgroundPath { get; init; } = "";
    public bool IsPinned { get; init; }
    public DateTimeOffset LastChanged { get; init; }
    public string BeatmapPath { get; init; } = "";
    public string MapsetName { get; init; } = "";
    public bool HasProjectMetadata { get; init; }
    public string MapsetFolderName => Path.GetFileName(Path.TrimEndingDirectorySeparator(BeatmapPath));
    public DateTime? LastChangedLocalDateTime => LastChanged == default ? null : LastChanged.LocalDateTime;
    public DirectoryInfo Directory => new(FullName);
}
