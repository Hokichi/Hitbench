namespace osuHitsounder.Services.FileSystem;

public interface ISampleFileService
{
    Task<IReadOnlyList<string>> ListAsync(string projectPath, CancellationToken token);
    Task<string> ImportAsync(string projectPath, string sourcePath, CancellationToken token);
    Task<string> RenameAsync(string projectPath, string fileName, string newStem, CancellationToken token);
    Task DeleteAsync(string projectPath, string fileName, CancellationToken token);
}
