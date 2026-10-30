using Hitbench.Resources.DataModels;

namespace Hitbench.Services.FileSystem;

public interface IHomeFolderService
{
    Task<FolderSnapshot> LoadProjectsAsync(CancellationToken cancellationToken);
    Task<FolderSnapshot?> LoadSongsAsync(string mapsetsDirectory, CancellationToken cancellationToken);
    Task<FolderSnapshot?> ReloadSongsAsync(string mapsetsDirectory, CancellationToken cancellationToken);
    Task<bool> IsInstallationAsync(string directory, CancellationToken cancellationToken);
    Task<OsuSearchResult> FindInstallationAsync(CancellationToken cancellationToken);
}
