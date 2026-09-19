using osuHitsounder.Core.Entities;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Presentation;

public sealed class StubWorkstationAudioService : IWorkstationAudioService
{
    public Func<string, CancellationToken, Task<WaveformData>> Analyze { get; set; } =
        (_, _) => Task.FromResult(new WaveformData(TimeSpan.FromSeconds(10), [0.5f]));
    public AudioPlaybackSnapshot Snapshot { get; set; } = new(TimeSpan.Zero, false, null);
    public string? OpenedPath { get; private set; }
    public bool Disposed { get; private set; }
    public Task<WaveformData> AnalyzeAsync(string audioPath, CancellationToken token) => Analyze(audioPath, token);
    public void Open(string audioPath) => OpenedPath = audioPath;
    public void Play() => Snapshot = Snapshot with { IsPlaying = true };
    public void Pause() => Snapshot = Snapshot with { IsPlaying = false };
    public void Stop() => Snapshot = Snapshot with { Position = TimeSpan.Zero, IsPlaying = false };
    public AudioPlaybackSnapshot GetSnapshot() => Snapshot;
    public void Dispose() => Disposed = true;
}
