using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Presentation;

public sealed class StubSampleBrowserService : ISampleBrowserService
{
    public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "SampleBrowserTests");
    public Dictionary<string, IReadOnlyList<SampleBrowserEntry>> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Func<string, CancellationToken, Task<IReadOnlyList<SampleBrowserEntry>>>? Listing { get; set; }
    public Func<string, CancellationToken, Task<SampleFileMetadata>>? Metadata { get; set; }
    public string DefaultDirectory { get; set; } = "";
    public string SavedDirectory => Path.Combine(RootPath, "saved");
    public Func<CancellationToken, Task<string>>? SavedDirectoryLoader { get; set; }
    public Task<string> GetExistingAncestorAsync(string directory, CancellationToken token)
    {
        string? candidate = Path.GetFullPath(directory);
        while (candidate is not null)
        {
            token.ThrowIfCancellationRequested();
            if (Entries.ContainsKey(candidate)) return Task.FromResult(candidate);
            candidate = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(candidate));
        }
        return Task.FromResult("");
    }
    public Task<string> GetSavedDirectoryAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Entries.TryAdd(SavedDirectory, []);
        return SavedDirectoryLoader?.Invoke(token) ?? Task.FromResult(SavedDirectory);
    }
    public IReadOnlyList<SampleBrowserEntry>? RootEntries { get; set; }
    public Func<CancellationToken, Task<IReadOnlyList<SampleBrowserEntry>>>? RootListing { get; set; }
    public List<string> RequestedDirectories { get; } = [];
    public int DefaultDirectoryRequests { get; private set; }
    public Func<string, CancellationToken, Task<Hitbench.Core.Entities.WaveformData>>? Waveform { get; set; }
    public Task<Hitbench.Core.Entities.WaveformData> ReadWaveformAsync(string path, CancellationToken token) => Waveform?.Invoke(path, token) ??
        Task.FromResult(new Hitbench.Core.Entities.WaveformData(TimeSpan.FromSeconds(1), new float[] { .2f, .5f, .1f }));
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
