using osuHitsounder.Services.ProjectFiles;

namespace osuHitsounder.Test.Presentation;

public sealed class DelayedProjectFileService : IProjectFileService
{
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? LatestJson { get; private set; }
    public bool FirstWriteStarted { get; private set; }
    public int Writes { get; private set; }
    public List<string> Paths { get; } = [];
    public List<string> Snapshots { get; } = [];
    public Task<IReadOnlySet<string>> GetExistingNamesAsync(CancellationToken token) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    public Task<string> CreateAsync(DirectoryInfo folder, string name, CancellationToken token) =>
        throw new NotSupportedException();
    public async Task SaveAsync(string path, string json, CancellationToken token)
    {
        Writes++;
        Paths.Add(path);
        Snapshots.Add(json);
        if (Writes == 1)
        {
            FirstWriteStarted = true;
            await release.Task;
        }
        LatestJson = json;
    }
    public void Release() => release.TrySetResult();
}
