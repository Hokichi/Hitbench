using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Presentation;

public sealed class StubSampleBrowserService : ISampleBrowserService
{
    public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "SampleBrowserTests");
    public Dictionary<string, IReadOnlyList<SampleBrowserEntry>> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Func<string, CancellationToken, Task<IReadOnlyList<SampleBrowserEntry>>>? Listing { get; set; }
    public Func<string, CancellationToken, Task<SampleFileMetadata>>? Metadata { get; set; }
    public string DefaultDirectory { get; set; } = "";
    public IReadOnlyList<SampleBrowserEntry>? RootEntries { get; set; }
    public Func<CancellationToken, Task<IReadOnlyList<SampleBrowserEntry>>>? RootListing { get; set; }
    public List<string> RequestedDirectories { get; } = [];
    public int DefaultDirectoryRequests { get; private set; }
    public Task<IReadOnlyList<SampleBrowserEntry>> GetRootsAsync(CancellationToken token) => RootListing?.Invoke(token) ?? Task.FromResult<IReadOnlyList<SampleBrowserEntry>>(RootEntries ?? [new(RootPath, "Root", true)]);
    public Task<string> GetDefaultDirectoryAsync(CancellationToken token)
    {
        DefaultDirectoryRequests++;
        return Task.FromResult(DefaultDirectory);
    }
    public Task<IReadOnlyList<SampleBrowserEntry>> GetEntriesAsync(string directory, CancellationToken token)
    {
        RequestedDirectories.Add(directory);
        return Listing?.Invoke(directory, token) ?? (Entries.TryGetValue(directory, out var entries) ? Task.FromResult(entries) : Task.FromException<IReadOnlyList<SampleBrowserEntry>>(new DirectoryNotFoundException("Folder unavailable")));
    }
    public Task<SampleFileMetadata> ReadMetadataAsync(string path, CancellationToken token) => Metadata?.Invoke(path, token) ??
        Task.FromResult(new SampleFileMetadata(path, Path.GetFileName(path), "WAV", 1000, TimeSpan.FromSeconds(1), 44_100, 1));
}
