using Soundie.Resources.DataModels;

namespace Soundie.Services.FileSystem;

public interface ISampleBrowserService
{
    Task<IReadOnlyList<SampleBrowserEntry>> GetRootsAsync(CancellationToken token);
    Task<string> GetDefaultDirectoryAsync(CancellationToken token);
    Task<IReadOnlyList<SampleBrowserEntry>> GetEntriesAsync(string directory, CancellationToken token);
    Task<SampleFileMetadata> ReadMetadataAsync(string path, CancellationToken token);
    Task<Soundie.Core.Entities.WaveformData> ReadWaveformAsync(string path, CancellationToken token);
}
