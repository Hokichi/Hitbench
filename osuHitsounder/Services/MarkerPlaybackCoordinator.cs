using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services;

/// <summary>Owns marker traversal; presentation never schedules sample triggers.</summary>
public sealed class MarkerPlaybackCoordinator(IWorkstationAudioService audio,
    Func<TimeSpan, CancellationToken, Task> delay) : IAsyncDisposable
{
    internal IWorkstationAudioService Audio => audio;
    private readonly SemaphoreSlim transitions = new(1);
    private readonly object snapshotLock = new();
    private readonly MarkerTraversal traversal = new();
    private readonly List<long> crossed = [];
    private PlaybackMarkerClock clock = new([], []);
    private TimeSpan duration;
    private TimeSpan processed;
    private CancellationTokenSource? cancellation;
    private Task worker = Task.CompletedTask;
    private long generation;
    private bool disposed;
    private AudioPlaybackSnapshot latest = new(TimeSpan.Zero, false, null);
    public event Action? StateChanged;

    public (long Generation, AudioPlaybackSnapshot Playback) Latest
    {
        get { lock (snapshotLock) return (generation, latest); }
    }

    public Task ConfigureAsync(PlaybackMarkerClock markerClock, TimeSpan songDuration,
        IReadOnlyList<SamplePlaybackEvent> events, CancellationToken token) => TransitionAsync(() =>
    {
        clock = markerClock;
        duration = songDuration;
        traversal.Replace(events);
        Reset(TimeSpan.Zero);
        lock (snapshotLock) generation++;
    }, token);

    public Task PlayAsync(CancellationToken token) => TransitionAsync(() =>
    {
        if (!clock.IsValid) throw new InvalidOperationException("Playback requires valid BPM timing data.");
        var snapshot = audio.GetSnapshot();
        if (snapshot.Position >= duration) { audio.Stop(); Reset(TimeSpan.Zero); }
        else if (snapshot.Position < processed || !snapshot.IsPlaying && snapshot.Error is not null) Reset(snapshot.Position);
        audio.Play();
    }, token);

    public Task PauseAsync(CancellationToken token) => TransitionAsync(audio.Pause, token);
    public Task StopAsync(CancellationToken token) => TransitionAsync(() => { audio.Stop(); Reset(TimeSpan.Zero); }, token);
    public Task SeekAsync(TimeSpan position, CancellationToken token) => TransitionAsync(() =>
    {
        audio.Seek(position);
        Reset(audio.GetSnapshot().Position);
    }, token);
    public Task ReplaceEventsAsync(IReadOnlyList<SamplePlaybackEvent> events, CancellationToken token) =>
        TransitionAsync(() => traversal.Replace(events), token);

    public async Task UpdateSamplesAsync(string path, IReadOnlyList<SamplePlaybackEvent> events, CancellationToken token)
    {
        Task preparation = Task.CompletedTask;
        await TransitionAsync(() =>
        {
            preparation = audio.UpdateSamplesAsync(path, events, token);
            traversal.Replace(events);
        }, token).ConfigureAwait(false);
        await preparation.ConfigureAwait(false);
    }

    private async Task TransitionAsync(Action change, CancellationToken token)
    {
        await transitions.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await QuiesceAsync().ConfigureAwait(false);
            change();
            Tick();
            if (audio.GetSnapshot().IsPlaying && clock.IsValid)
            {
                cancellation = new CancellationTokenSource();
                var workerToken = cancellation.Token;
                worker = Task.Run(() => RunAsync(workerToken), CancellationToken.None);
            }
        }
        finally { transitions.Release(); }
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && Latest.Playback.IsPlaying)
            {
                // No UI dispatch and no busy spin. Re-read the output clock after every wake.
                var position = audio.GetSnapshot().Position;
                await delay(clock.GetDelayToNextBoundary(position), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                Tick();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            try { audio.Pause(); } catch { }
            Publish(new AudioPlaybackSnapshot(processed, false, error.Message));
        }
    }

    private void Tick()
    {
        var snapshot = audio.GetSnapshot();
        if (snapshot.IsPlaying && clock.IsValid)
        {
            var marker = clock.GetPosition(snapshot.Position, duration);
            if (marker < processed) marker = processed;
            traversal.Advance(marker, crossed);
            if (crossed.Count != 0) audio.TriggerSamples(crossed);
            processed = marker;
            snapshot = snapshot with { Position = marker };
        }
        else if (snapshot.Error is not null) Reset(snapshot.Position);
        Publish(snapshot);
    }

    private void Reset(TimeSpan position)
    {
        processed = position;
        traversal.Reset(position, true);
    }

    private void Publish(AudioPlaybackSnapshot value)
    {
        bool notify;
        lock (snapshotLock)
        {
            notify = latest.IsPlaying != value.IsPlaying || latest.Error != value.Error || latest.Warning != value.Warning;
            latest = value;
        }
        if (notify) StateChanged?.Invoke();
    }

    private async Task QuiesceAsync()
    {
        cancellation?.Cancel();
        await worker.ConfigureAwait(false);
        cancellation?.Dispose();
        cancellation = null;
        worker = Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await transitions.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            await QuiesceAsync().ConfigureAwait(false);
            StateChanged = null;
        }
        finally { transitions.Release(); }
    }
}
