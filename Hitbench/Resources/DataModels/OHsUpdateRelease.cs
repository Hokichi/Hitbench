namespace Hitbench.Resources.DataModels;

public sealed record OHsUpdateRelease(OHsReleaseVersion Version, DateTimeOffset PublishedAt, long ReleaseId,
    string SourceCommit, Uri AssetUri, long AssetSize, string? AssetDigest);
