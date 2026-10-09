using osuHitsounder.Core.Entities;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace osuHitsounder.Services;

/// <summary>Publishes the played-position marker and coordinates transport independently of sample rendering.</summary>
public sealed class MarkerPlaybackCoordinator(IWorkstationAudioService audio,
    Func<TimeSpan, CancellationToken, Task> delay) : IAsyncDisposable
{
    // Windows per-wait precision; no busy spin or process/global timer-resolution change.
    public static Task DelayAsync(TimeSpan duration, CancellationToken token) => Task.Run(() =>
    {
        if (token.IsCancellationRequested) return;
        using var timer = new EventWaitHandle(false, EventResetMode.AutoReset);
        var handle = CreateWaitableTimerExW(IntPtr.Zero, null, 2, 0x001F0003);
        if (handle.IsInvalid) { handle.Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        var original = timer.SafeWaitHandle;
        timer.SafeWaitHandle = handle;
        original.Dispose();
        var due = -Math.Max(1, duration.Ticks);
        if (!SetWaitableTimer(handle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        WaitHandle.WaitAny([timer, token.WaitHandle]);
    }, CancellationToken.None);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, IntPtr callback, IntPtr argument,
        [MarshalAs(UnmanagedType.Bool)] bool resume);

    internal IWorkstationAudioService Audio => audio;
    private readonly SemaphoreSlim transitions = new(1);
    private readonly object snapshotLock = new();
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
        CancellationToken token) => TransitionAsync(() =>
    {
        clock = markerClock;
        duration = songDuration;
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

    public async Task UpdateSamplesAsync(string path, IReadOnlyList<SamplePlaybackEvent> events, CancellationToken token)
    {
        Task preparation = Task.CompletedTask;
        await TransitionAsync(() => preparation = audio.UpdateSamplesAsync(path, events, token), token).ConfigureAwait(false);
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
        catch (Exception error) when (!disposed)
        {
            try { audio.Pause(); } catch { }
            Reset(processed);
            Publish(new AudioPlaybackSnapshot(processed, false, error.Message));
            throw;
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
                if (token.IsCancellationRequested) break;
                Tick();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            try { audio.Pause(); } catch { }
            Reset(processed);
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
            processed = marker;
            snapshot = snapshot with { Position = marker };
        }
        else if (snapshot.Error is not null) Reset(snapshot.Position);
        Publish(snapshot);
    }

    private void Reset(TimeSpan position)
    {
        processed = position;
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
