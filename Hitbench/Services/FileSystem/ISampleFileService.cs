namespace Hitbench.Services.FileSystem;

public interface ISampleFileService
{
    Task<string> SaveAsync(string projectPath, string fileName, CancellationToken token);
    Task<bool> TryMigrateSavedSamplesAsync(string projectPath, CancellationToken token);
    Task<IReadOnlyList<string>> ListAsync(string projectPath, CancellationToken token, bool includeSubfolders = true);
    Task<string> ImportAsync(string projectPath, string sourcePath, CancellationToken token, string? folderName = null);
    Task<string> ImportKeyAsync(string projectPath, string sourcePath, string folderName, string key, CancellationToken token);
    Task<string> RenameAsync(string projectPath, string fileName, string newStem, CancellationToken token);
    Task DeleteAsync(string projectPath, string fileName, CancellationToken token);
    Task<string> EnsureFolderAsync(string projectPath, string trackName, string currentFolderName, IReadOnlyCollection<string> reservedFolderNames, CancellationToken token);
    Task<string> RenameFolderAsync(string projectPath, string folderName, string trackName, IReadOnlyCollection<string> reservedFolderNames, CancellationToken token);
    Task DeleteEmptyFolderAsync(string projectPath, string folderName, CancellationToken token);
    Task<(IReadOnlyDictionary<string, IReadOnlyList<string>> Candidates, IReadOnlyList<string> IgnoredPaths)> FindNoteFilesAsync(string sourceDirectory, CancellationToken token);
}
