using osuHitsounder.Core.Entities;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Presentation;

public sealed class StubWorkstationAudioService : IWorkstationAudioService
{
    public Func<string, CancellationToken, Task<WaveformData>> Analyze { get; set; } =
        (_, _) => Task.FromResult(new WaveformData(TimeSpan.FromSeconds(10), [0.5f]));
    private readonly object sync = new();
    private AudioPlaybackSnapshot snapshot = new(TimeSpan.Zero, false, null);
    public AudioPlaybackSnapshot Snapshot { get { lock(sync) return snapshot; } set { lock(sync) snapshot = value; } }
    public static async Task WaitUntilAsync(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!ready()) await Task.Delay(1, timeout.Token);
    }
    public string? OpenedPath { get; private set; }
    public bool Disposed { get; private set; }
    public int SongVolume { get; private set; } = 100;
    public int SamplesVolume { get; private set; } = 100;
    public int ApplicationVolume { get; private set; } = 100;
    private readonly List<IReadOnlyList<SamplePlaybackEvent>> sampleUpdates = [];
    public IReadOnlyList<IReadOnlyList<SamplePlaybackEvent>> SampleUpdates { get { lock(sync) return sampleUpdates.ToArray(); } }
    public Func<CancellationToken, Task>? PrepareSamplesAsync { get; set; }
    public Task<WaveformData> AnalyzeAsync(string audioPath, CancellationToken token) => Analyze(audioPath, token);
    public void Open(string audioPath) => OpenedPath = audioPath;
    public Action? Playing { get; set; }
    public Action? ReadingSnapshot { get; set; }
    public void Play() { Playing?.Invoke(); Snapshot = Snapshot with { IsPlaying = true }; }
    public void Pause() => Snapshot = Snapshot with { IsPlaying = false };
    public void Stop() => Snapshot = Snapshot with { Position = TimeSpan.Zero, IsPlaying = false };
    public void Seek(TimeSpan position) => Snapshot = Snapshot with { Position = position };
    public void SetSongVolume(int volume) => SongVolume = volume;
    public void SetSamplesVolume(int volume) => SamplesVolume = volume;
    public void SetApplicationVolume(int volume) => ApplicationVolume = volume;
    public Task UpdateSamplesAsync(string projectPath, IReadOnlyList<SamplePlaybackEvent> events, CancellationToken token)
    {
        lock(sync) sampleUpdates.Add(events.ToArray());
        return PrepareSamplesAsync?.Invoke(token) ?? Task.CompletedTask;
    }
    private int snapshotReads;
    public int SnapshotReads => Volatile.Read(ref snapshotReads);
    public AudioPlaybackSnapshot GetSnapshot()
    {
        ReadingSnapshot?.Invoke();
        Interlocked.Increment(ref snapshotReads);
        return Snapshot;
    }
    public void Dispose() => Disposed = true;
}
