namespace Hitbench.Resources.DataModels;

public sealed record OHsUpdateManifest(string Version, string Commit, IReadOnlyList<string> Files);
