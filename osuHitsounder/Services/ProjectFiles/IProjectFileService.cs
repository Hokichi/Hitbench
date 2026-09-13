using System.IO;

namespace osuHitsounder.Services.ProjectFiles;

public interface IProjectFileService
{
    Task<IReadOnlySet<string>> GetExistingNamesAsync(CancellationToken cancellationToken);
    Task<string> CreateAsync(DirectoryInfo folder, string projectName, CancellationToken cancellationToken);
}
