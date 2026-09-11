using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Presentation;

public sealed class StubHomeFolderService(Func<CancellationToken, Task<HomeFolderData>> load) : IHomeFolderService
{
    public Task<HomeFolderData> LoadAsync(CancellationToken cancellationToken) => load(cancellationToken);
}
