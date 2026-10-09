using osuHitsounder.Services;

namespace osuHitsounder.Test.Presentation;

public sealed class StubSamplePreviewService : ISamplePreviewService
{
    public bool Playing { get; private set; }
    public bool Disposed { get; private set; }
    public Func<CancellationToken, Task>? Play { get; set; }
    public Func<CancellationToken, Task>? Seek { get; set; }
    public Func<CancellationToken, Task<SamplePreviewSnapshot>>? Snapshot { get; set; }
    public List<string> PlayedPaths { get; } = [];
    public List<TimeSpan> SeekPositions { get; } = [];
    public TimeSpan StartPosition { get; private set; }
    public TimeSpan Position { get; set; }
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(1);
    public async Task PlayAsync(string path, int applicationVolume, CancellationToken token, TimeSpan startPosition = default)
    {
        if (Play is not null) await Play(token);
        token.ThrowIfCancellationRequested();
        PlayedPaths.Add(path);
        Position = StartPosition = startPosition;
        Playing = true;
    }
    public Task StopAsync(CancellationToken token) { Playing = false; return Task.CompletedTask; }
    public async Task SeekAsync(TimeSpan position, CancellationToken token)
    {
        if (Seek is not null) await Seek(token);
        token.ThrowIfCancellationRequested();
        SeekPositions.Add(position);
        Position = position;
    }
    public Task<SamplePreviewSnapshot> GetSnapshotAsync(CancellationToken token) => Snapshot?.Invoke(token) ??
        Task.FromResult(new SamplePreviewSnapshot(Playing, null, null, Position, Duration));
    public ValueTask DisposeAsync() { Disposed = true; Playing = false; return ValueTask.CompletedTask; }
}
