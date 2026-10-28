using Hitbench.Core.Entities;
using Hitbench.Services;

namespace Hitbench.Test.Presentation;

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
    public Action? Stopping { get; set; }
    public bool IsPrepared { get; set; } = true;
    public int SongVolume { get; private set; } = 100;
    public int SamplesVolume { get; private set; } = 100;
    public int ApplicationVolume { get; private set; } = 100;
    public IReadOnlyList<ControlPointVolumeSnapshot> ControlPoints { get; private set; } = [];
    public void SetControlPoints(IReadOnlyList<ControlPointVolumeSnapshot> points) => ControlPoints = Array.AsReadOnly(points.ToArray());
    private readonly List<IReadOnlyList<SamplePlaybackEvent>> sampleUpdates = [];
    public IReadOnlyList<IReadOnlyList<SamplePlaybackEvent>> SampleUpdates { get { lock(sync) return sampleUpdates.ToArray(); } }
    public Func<CancellationToken, Task>? PrepareSamplesAsync { get; set; }
    public Task<WaveformData> AnalyzeAsync(string audioPath, CancellationToken token) => Analyze(audioPath, token);
    public void Open(string audioPath) => OpenedPath = audioPath;
    public Action? Playing { get; set; }
    public Action? ReadingSnapshot { get; set; }
    public int PlayCalls { get; private set; }
    public int PauseCalls { get; private set; }
    public int SeekCalls { get; private set; }
    public void Play() { PlayCalls++; Playing?.Invoke(); Snapshot = Snapshot with { IsPlaying = true }; }
    public void Pause() { PauseCalls++; Snapshot = Snapshot with { IsPlaying = false }; }
    public void Stop() { Stopping?.Invoke(); Snapshot = Snapshot with { Position = TimeSpan.Zero, IsPlaying = false }; }
    public void Seek(TimeSpan position) { SeekCalls++; Snapshot = Snapshot with { Position = position }; }
    public void SetSongVolume(int volume) => SongVolume = volume;
    public void SetSamplesVolume(int volume) => SamplesVolume = volume;
    public void SetApplicationVolume(int volume) => ApplicationVolume = volume;
    public int ForceReloadSampleRequests { get; private set; }
    public Task UpdateSamplesAsync(string projectPath, IReadOnlyList<SamplePlaybackEvent> events, CancellationToken token, bool forceReload = false)
    {
        lock(sync)
        {
            sampleUpdates.Add(events.ToArray());
            if (forceReload) ForceReloadSampleRequests++;
        }
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
