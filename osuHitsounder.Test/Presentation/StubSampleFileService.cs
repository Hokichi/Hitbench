using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Presentation;

public sealed class StubSampleFileService(IReadOnlyList<string> files) : ISampleFileService
{
    public Func<string, string, CancellationToken, Task<string>>? Import { get; set; }
    public Func<string, CancellationToken, Task<IReadOnlyList<string>>>? List { get; set; }
    public Task<IReadOnlyList<string>> ListAsync(string projectPath, CancellationToken token) =>
        List?.Invoke(projectPath, token) ?? Task.FromResult(files);

    public Task<string> ImportAsync(string projectPath, string sourcePath, CancellationToken token) =>
        Import?.Invoke(projectPath, sourcePath, token) ?? Task.FromResult(Path.GetFileName(sourcePath));
}
