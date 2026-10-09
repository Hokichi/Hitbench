namespace osuHitsounder.Services;

public interface ISamplePreviewService : IAsyncDisposable
{
    Task PlayAsync(string path, int applicationVolume, CancellationToken token);
    Task StopAsync(CancellationToken token);
    Task<SamplePreviewSnapshot> GetSnapshotAsync(CancellationToken token);
}
