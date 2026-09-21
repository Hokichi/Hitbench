using osuHitsounder.Services.ProjectFiles;

namespace osuHitsounder.Test.Presentation;

public sealed class StubProjectFileService(
    Func<DirectoryInfo, string, CancellationToken, Task<string>> create,
    IReadOnlySet<string>? existingNames = null) : IProjectFileService
{
    public Task SaveAsync(string projectPath, string json, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlySet<string>> GetExistingNamesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(existingNames ?? (IReadOnlySet<string>)new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public Task<string> CreateAsync(DirectoryInfo folder, string projectName, CancellationToken cancellationToken) =>
        create(folder, projectName, cancellationToken);
}
