using Hitbench.Services.ProjectFiles;

namespace Hitbench.Test.Presentation;

public sealed class StubProjectFileService(
    Func<DirectoryInfo, string, CancellationToken, Task<string>> create,
    IReadOnlySet<string>? existingNames = null) : IProjectFileService
{
    public Func<string, string, CancellationToken, Task> Save { get; set; } = (_, _, _) => Task.CompletedTask;
    public Func<string, bool, CancellationToken, Task> Pin { get; set; } = (_, _, _) => Task.CompletedTask;
    public Func<string, CancellationToken, Task> Delete { get; set; } = (_, _) => Task.CompletedTask;
    public Task SaveAsync(string projectPath, string json, CancellationToken cancellationToken) => Save(projectPath, json, cancellationToken);
    public Task SetPinnedAsync(string path, bool pinned, CancellationToken token) => Pin(path, pinned, token);
    public Task DeleteAsync(string path, CancellationToken token) => Delete(path, token);
    public Task<IReadOnlySet<string>> GetExistingNamesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(existingNames ?? (IReadOnlySet<string>)new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public Task<string> CreateAsync(DirectoryInfo folder, string projectName, CancellationToken cancellationToken) =>
        create(folder, projectName, cancellationToken);
}
