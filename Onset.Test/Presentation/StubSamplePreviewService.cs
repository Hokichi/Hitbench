using Onset.Services;

namespace Onset.Test.Presentation;

public sealed class StubSamplePreviewService : ISamplePreviewService
{
    public bool Playing { get; private set; }
    public bool Disposed { get; private set; }
    public int StopCalls { get; private set; }
    public int DisposeCalls { get; private set; }
    public List<int> PlayedVolumes { get; } = [];
    public List<string> Events { get; } = [];
    public bool IgnorePlayCancellation { get; set; }
    public Func<Task>? Stop { get; set; }
    public Func<Task>? Dispose { get; set; }
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
        if (!IgnorePlayCancellation) token.ThrowIfCancellationRequested();
        PlayedPaths.Add(path);
        PlayedVolumes.Add(applicationVolume);
        Events.Add("play");
        Position = StartPosition = startPosition;
        Playing = true;
    }
    public async Task StopAsync(CancellationToken token) { StopCalls++; Events.Add("stop"); if (Stop is not null) await Stop(); Playing = false; }
    public async Task SeekAsync(TimeSpan position, CancellationToken token)
    {
        if (Seek is not null) await Seek(token);
        token.ThrowIfCancellationRequested();
        SeekPositions.Add(position);
        Position = position;
    }
    public Task<SamplePreviewSnapshot> GetSnapshotAsync(CancellationToken token) => Snapshot?.Invoke(token) ??
        Task.FromResult(new SamplePreviewSnapshot(Playing, null, null, Position, Duration));
    public async ValueTask DisposeAsync() { DisposeCalls++; if (Dispose is not null) await Dispose(); Disposed = true; Playing = false; }
}
