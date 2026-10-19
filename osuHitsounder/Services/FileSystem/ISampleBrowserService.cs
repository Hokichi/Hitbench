using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Services.FileSystem;

public interface ISampleBrowserService
{
    Task<IReadOnlyList<SampleBrowserEntry>> GetRootsAsync(CancellationToken token);
    Task<string> GetDefaultDirectoryAsync(CancellationToken token);
    Task<IReadOnlyList<SampleBrowserEntry>> GetEntriesAsync(string directory, CancellationToken token);
    Task<SampleFileMetadata> ReadMetadataAsync(string path, CancellationToken token);
    Task<osuHitsounder.Core.Entities.WaveformData> ReadWaveformAsync(string path, CancellationToken token);
}
