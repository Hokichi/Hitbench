using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Services.FileSystem;

public interface IHomeFolderService
{
    Task<HomeFolderData> LoadAsync(CancellationToken cancellationToken);
}
