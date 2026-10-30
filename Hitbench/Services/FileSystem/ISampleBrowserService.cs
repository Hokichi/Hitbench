using Hitbench.Resources.DataModels;

namespace Hitbench.Services.FileSystem;

public interface ISampleBrowserService
{
    Task<string> GetExistingAncestorAsync(string directory, CancellationToken token);
    Task<string> GetSavedDirectoryAsync(CancellationToken token);
    Task<IReadOnlyList<SampleBrowserEntry>> GetRootsAsync(CancellationToken token);
    Task<string> GetDefaultDirectoryAsync(CancellationToken token);
    Task<IReadOnlyList<SampleBrowserEntry>> GetEntriesAsync(string directory, CancellationToken token);
    Task<SampleFileMetadata> ReadMetadataAsync(string path, CancellationToken token);
    Task<Hitbench.Core.Entities.WaveformData> ReadWaveformAsync(string path, CancellationToken token);
}
