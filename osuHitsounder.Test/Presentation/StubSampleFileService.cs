using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Presentation;

public sealed class StubSampleFileService(IReadOnlyList<string> files) : ISampleFileService
{
    public Task<IReadOnlyList<string>> ListAsync(string projectPath, CancellationToken token) =>
        Task.FromResult(files);

    public Task<string> ImportAsync(string projectPath, string sourcePath, CancellationToken token) =>
        Task.FromResult(Path.GetFileName(sourcePath));
}
