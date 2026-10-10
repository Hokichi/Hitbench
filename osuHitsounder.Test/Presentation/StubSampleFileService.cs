using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Presentation;

public sealed class StubSampleFileService(IReadOnlyList<string> files) : ISampleFileService
{
    public Func<string, string, string, CancellationToken, Task<string>>? Rename { get; set; }
    public Func<string, string, CancellationToken, Task>? Delete { get; set; }
    public Task<string> RenameAsync(string projectPath, string fileName, string newStem, CancellationToken token) =>
        Rename?.Invoke(projectPath, fileName, newStem, token) ?? Task.FromResult(newStem + Path.GetExtension(fileName));
    public Task DeleteAsync(string projectPath, string fileName, CancellationToken token) =>
        Delete?.Invoke(projectPath, fileName, token) ?? Task.CompletedTask;
    public Func<string, string, CancellationToken, Task<string>>? Import { get; set; }
    public Func<string, CancellationToken, Task<IReadOnlyList<string>>>? List { get; set; }
    public Task<IReadOnlyList<string>> ListAsync(string projectPath, CancellationToken token) =>
        List?.Invoke(projectPath, token) ?? Task.FromResult(files);

    public Task<string> ImportAsync(string projectPath, string sourcePath, CancellationToken token) =>
        Import?.Invoke(projectPath, sourcePath, token) ?? Task.FromResult(Path.GetFileName(sourcePath));
}
