namespace osuHitsounder.Resources.DataModels;

public sealed class MappingConflict(string key, string? existingPath, IReadOnlyList<string> incomingPaths, bool existingIsMissing = false)
{
    public string Key { get; } = key;
    public string? ExistingPath { get; } = existingPath;
    public IReadOnlyList<string> IncomingPaths { get; } = Array.AsReadOnly(incomingPaths.ToArray());
    public bool ExistingIsMissing { get; } = existingIsMissing;
}
