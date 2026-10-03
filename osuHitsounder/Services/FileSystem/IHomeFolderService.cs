using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Services.FileSystem;

public interface IHomeFolderService
{
    Task<FolderSnapshot> LoadProjectsAsync(CancellationToken cancellationToken);
    Task<FolderSnapshot?> LoadSongsAsync(string osuDirectory, CancellationToken cancellationToken);
    Task<FolderSnapshot?> ReloadSongsAsync(string osuDirectory, CancellationToken cancellationToken);
    Task<bool> IsInstallationAsync(string directory, CancellationToken cancellationToken);
    Task<OsuSearchResult> FindInstallationAsync(CancellationToken cancellationToken);
}
