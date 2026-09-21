namespace osuHitsounder.Services.FileSystem;

public interface ISampleFileService
{
    Task<IReadOnlyList<string>> ListAsync(string projectPath, CancellationToken token);
    Task<string> ImportAsync(string projectPath, string sourcePath, CancellationToken token);
}
