using osuHitsounder.Services;

namespace osuHitsounder.Test.Presentation;

public sealed class StubSamplePreviewService : ISamplePreviewService
{
    public bool Playing { get; private set; }
    public bool Disposed { get; private set; }
    public Func<CancellationToken, Task>? Play { get; set; }
    public async Task PlayAsync(string path, int applicationVolume, CancellationToken token)
    {
        if (Play is not null) await Play(token);
        token.ThrowIfCancellationRequested();
        Playing = true;
    }
    public Task StopAsync(CancellationToken token) { Playing = false; return Task.CompletedTask; }
    public Task<SamplePreviewSnapshot> GetSnapshotAsync(CancellationToken token) => Task.FromResult(new SamplePreviewSnapshot(Playing, null, null));
    public ValueTask DisposeAsync() { Disposed = true; Playing = false; return ValueTask.CompletedTask; }
}
