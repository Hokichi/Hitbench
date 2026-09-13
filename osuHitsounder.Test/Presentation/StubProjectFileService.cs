using osuHitsounder.Services.ProjectFiles;

namespace osuHitsounder.Test.Presentation;

public sealed class StubProjectFileService(Func<DirectoryInfo, CancellationToken, Task<string>> create) : IProjectFileService
{
    public Task<string> CreateAsync(DirectoryInfo folder, CancellationToken cancellationToken) => create(folder, cancellationToken);
}
