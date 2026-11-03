namespace Hitbench.Resources.DataModels;

public sealed record OHsStagedUpdate(string Version, string Workspace, string PayloadDirectory, IReadOnlyList<string> Files);
