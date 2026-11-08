namespace Hitbench.Resources.DataModels;

public sealed record SampleManagementContext(string ProjectPath, string? InitialDirectory, Guid SessionId, bool HasMapsetFolderConflict = false);
