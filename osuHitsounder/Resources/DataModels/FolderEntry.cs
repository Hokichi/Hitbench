using System.IO;

namespace osuHitsounder.Resources.DataModels;

public sealed record FolderEntry(string FullName, string Name, DateTimeOffset LastWriteTimeUtc)
{
    public string DisplayName { get; init; } = Name;
    public string Artist { get; init; } = "";
    public string Title { get; init; } = "";
    public string BackgroundPath { get; init; } = "";
    public DirectoryInfo Directory => new(FullName);
}
