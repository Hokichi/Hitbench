using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Presentation;

public sealed class StubSampleFileService(IReadOnlyList<string> files) : ISampleFileService
{
    public Func<string, string, CancellationToken, Task<string>>? Save { get; set; }
    public Task<string> SaveAsync(string projectPath, string fileName, CancellationToken token) =>
        Save?.Invoke(projectPath, fileName, token) ?? Task.FromResult(fileName);
    public Task<bool> TryMigrateSavedSamplesAsync(string projectPath, CancellationToken token) => Task.FromResult(true);
    public Func<string, string, string, CancellationToken, Task<string>>? Rename { get; set; }
    public Func<string, string, CancellationToken, Task>? Delete { get; set; }
    public Task<string> RenameAsync(string projectPath, string fileName, string newStem, CancellationToken token) =>
        Rename?.Invoke(projectPath, fileName, newStem, token) ?? Task.FromResult(newStem + Path.GetExtension(fileName));
    public Task DeleteAsync(string projectPath, string fileName, CancellationToken token) =>
        Delete?.Invoke(projectPath, fileName, token) ?? Task.CompletedTask;
    public Func<string, string, CancellationToken, Task<string>>? Import { get; set; }
    public Func<string, CancellationToken, Task<IReadOnlyList<string>>>? List { get; set; }
    public async Task<IReadOnlyList<string>> ListAsync(string projectPath, CancellationToken token, bool includeSubfolders = true)
    {
        var result = List is null ? files : await List(projectPath, token);
        token.ThrowIfCancellationRequested();
        return includeSubfolders ? result : result.Where(name => !name.Contains('/') && !name.Contains('\\')).ToArray();
    }

    public Task<string> ImportAsync(string projectPath, string sourcePath, CancellationToken token, string? folderName = null) =>
        Import?.Invoke(projectPath, sourcePath, token) ?? Task.FromResult(folderName is null ? Path.GetFileName(sourcePath) : folderName + "/" + Path.GetFileName(sourcePath));
    public Func<string, string, string, IReadOnlyCollection<string>, CancellationToken, Task<string>>? FolderRename { get; set; }
    public Task<string> EnsureFolderAsync(string projectPath, string trackName, string currentFolderName, IReadOnlyCollection<string> reserved, CancellationToken token) =>
        Task.FromResult(currentFolderName.Length > 0 ? currentFolderName : SamplePaths.NormalizeFolderName(trackName));
    public Task<string> RenameFolderAsync(string projectPath, string folderName, string trackName, IReadOnlyCollection<string> reserved, CancellationToken token) =>
        FolderRename?.Invoke(projectPath, folderName, trackName, reserved, token) ?? Task.FromResult(SamplePaths.NormalizeFolderName(trackName));
    public Task DeleteEmptyFolderAsync(string projectPath, string folderName, CancellationToken token) => Task.CompletedTask;
    public Task<(IReadOnlyDictionary<string, IReadOnlyList<string>> Candidates, IReadOnlyList<string> IgnoredPaths)> FindNoteFilesAsync(string sourceDirectory, CancellationToken token) =>
        new SampleFileService().FindNoteFilesAsync(sourceDirectory, token);
}
