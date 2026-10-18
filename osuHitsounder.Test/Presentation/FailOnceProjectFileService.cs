using osuHitsounder.Services.ProjectFiles;

namespace osuHitsounder.Test.Presentation;

public sealed class FailOnceProjectFileService : IProjectFileService
{
    public Task SetPinnedAsync(string path, bool pinned, CancellationToken token) => throw new NotSupportedException();
    public Task DeleteAsync(string path, CancellationToken token) => throw new NotSupportedException();
    public string? SavedJson { get; private set; }
    public Task<IReadOnlySet<string>> GetExistingNamesAsync(CancellationToken token) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    public Task<string> CreateAsync(DirectoryInfo folder, string name, CancellationToken token) =>
        throw new NotSupportedException();
    public Task SaveAsync(string path, string json, CancellationToken token)
    {
        if (SavedJson is null && !failed)
        {
            failed = true;
            throw new IOException("Disk full");
        }
        SavedJson = json;
        return Task.CompletedTask;
    }
    private bool failed;
}
