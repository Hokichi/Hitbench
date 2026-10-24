using System.IO;

namespace Hitbench.Services.ProjectFiles;

public interface IProjectFileService
{
    Task<IReadOnlySet<string>> GetExistingNamesAsync(CancellationToken cancellationToken);
    Task<string> CreateAsync(DirectoryInfo folder, string projectName, CancellationToken cancellationToken);
    Task SaveAsync(string projectPath, string json, CancellationToken cancellationToken);
    Task SetPinnedAsync(string projectPath, bool isPinned, CancellationToken cancellationToken);
    Task DeleteAsync(string projectPath, CancellationToken cancellationToken);
}
