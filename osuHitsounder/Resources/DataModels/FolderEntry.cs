using System.IO;

namespace osuHitsounder.Resources.DataModels;

public sealed record FolderEntry(string FullName, string Name, DateTimeOffset LastWriteTimeUtc)
{
    public string DisplayName { get; init; } = Name;
    public DirectoryInfo Directory => new(FullName);
}
