using Onset.Resources.DataModels;
using Onset.Services.FileSystem;

namespace Onset.Test.Presentation;

public sealed class StubHomeFolderService : IHomeFolderService
{
    public StubHomeFolderService() { }
    public StubHomeFolderService(Func<CancellationToken, Task<HomeFolderData>> load)
    {
        Projects = async token => Snapshot((await load(token)).Projects);
        Songs = async (_, token) =>
        {
            var data = await load(token);
            return data.SongsFolderExists ? Snapshot(data.Songs) : null;
        };
    }
    private static FolderSnapshot Snapshot(IReadOnlyList<DirectoryInfo> rows) =>
        new(rows.Select(row => new FolderEntry(row.FullName, row.Name, DateTimeOffset.MinValue)));
    public Func<CancellationToken, Task<FolderSnapshot>> Projects { get; set; } =
        _ => Task.FromResult(new FolderSnapshot([]));
    public Func<string, CancellationToken, Task<FolderSnapshot?>> Songs { get; set; } =
        (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot([]));
    public Func<string, CancellationToken, Task<bool>> Valid { get; set; } = (_, _) => Task.FromResult(true);
    public Func<CancellationToken, Task<OsuSearchResult>> Find { get; set; } =
        _ => Task.FromResult(new OsuSearchResult(null, false));
    public Task<FolderSnapshot> LoadProjectsAsync(CancellationToken token) => Projects(token);
    public Task<FolderSnapshot?> LoadSongsAsync(string path, CancellationToken token) => Songs(path, token);
    public Func<string, CancellationToken, Task<FolderSnapshot?>>? ReloadSongs { get; set; }
    public Task<FolderSnapshot?> ReloadSongsAsync(string path, CancellationToken token) => (ReloadSongs ?? Songs)(path, token);
    public Task<bool> IsInstallationAsync(string path, CancellationToken token) => Valid(path, token);
    public Task<OsuSearchResult> FindInstallationAsync(CancellationToken token) => Find(token);
}
