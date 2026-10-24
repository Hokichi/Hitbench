namespace Onset.Services;

public interface ISamplePreviewService : IAsyncDisposable
{
    Task PlayAsync(string path, int applicationVolume, CancellationToken token, TimeSpan startPosition = default);
    Task SeekAsync(TimeSpan position, CancellationToken token);
    Task StopAsync(CancellationToken token);
    Task<SamplePreviewSnapshot> GetSnapshotAsync(CancellationToken token);
}
