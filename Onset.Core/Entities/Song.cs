namespace Onset.Core.Entities;

public sealed record Song(string FullName, string Name, DateTimeOffset LastWriteTimeUtc,
    string Artist, string Title, string DisplayName, string BackgroundPath);
