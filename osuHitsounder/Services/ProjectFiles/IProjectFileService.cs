using System.IO;

namespace osuHitsounder.Services.ProjectFiles;

public interface IProjectFileService
{
    Task<string> CreateAsync(DirectoryInfo folder, CancellationToken cancellationToken);
}
