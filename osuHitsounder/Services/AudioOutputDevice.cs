using NAudio.Wave;
using System.Diagnostics;

namespace osuHitsounder.Services;

public sealed class AudioOutputDevice : IAudioOutput
{
    private readonly Func<IWavePlayer> createPlayer;
    private readonly Func<Action, IDisposable>? startMonitor;
    private IWavePlayer? player;
    private IDisposable? monitor;
    private StoppedEventArgs? pendingStop;
    private long originBytes;
    private long playedFrames;
    private int bytesPerFrame;
    private bool disposed;
    private int ownerThread;
    private readonly Func<int>? getLatencyFrames;
    private readonly Func<bool>? resetRequested;
    private readonly Func<long> getTimestamp;
    private readonly long timestampFrequency;
    private Func<long>? renderedFrames;
    private Func<bool>? endOfStream;
    private Func<bool>? faulted;
    private int reset;
    private int renderFault;
    private int latencyFrames;
    private int sampleRate;
    private bool estimated;
    private long lastProduced = -1;
    private long positionAnchor;
    private long timeAnchor;

    public AudioOutputDevice(Func<IWavePlayer> createPlayer, Func<Action, IDisposable>? startMonitor = null,
        Func<int>? getLatencyFrames = null, Func<bool>? resetRequested = null,
        Func<long>? getTimestamp = null, long timestampFrequency = 0)
    {
        this.createPlayer = createPlayer;
        this.startMonitor = startMonitor;
        this.getLatencyFrames = getLatencyFrames;
        this.resetRequested = resetRequested;
        this.getTimestamp = getTimestamp ?? Stopwatch.GetTimestamp;
        this.timestampFrequency = timestampFrequency > 0 ? timestampFrequency : Stopwatch.Frequency;
    }

    public PlaybackState PlaybackState { get; private set; }
    public string? Warning => null;
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public void Init(ISampleProvider source, Func<long>? renderedFrames = null, Func<bool>? endOfStream = null, Func<bool>? faulted = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (player is not null) throw new InvalidOperationException("Output is already initialized.");
        ownerThread = Environment.CurrentManagedThreadId;
        this.renderedFrames = renderedFrames;
        this.endOfStream = endOfStream;
        this.faulted = faulted;
        sampleRate = source.WaveFormat.SampleRate;
        try
        {
            player = createPlayer();
            player.PlaybackStopped += OnStopped;
            if (player is AsioOut asio)
            {
                asio.AutoStop = false;
                asio.DriverResetRequest += OnReset;
                if (!asio.IsSampleRateSupported(sampleRate))
                    throw new NotSupportedException("ASIO driver does not support 44,100 Hz.");
            }
            bytesPerFrame = source.WaveFormat.Channels * sizeof(float);
            player.Init(new GuardedSampleProvider(source,
                () => Volatile.Read(ref reset) != 0 || Volatile.Read(ref renderFault) != 0 || (this.faulted?.Invoke() ?? false),
                () => Volatile.Write(ref renderFault, 1)).ToWaveProvider());
            originBytes = player is IWavePosition position ? position.GetPosition() : 0;
            estimated = player is not IWavePosition || getLatencyFrames is not null;
            latencyFrames = Math.Max(0, getLatencyFrames?.Invoke() ?? (player is AsioOut initialized ? initialized.PlaybackLatency : 0));
            monitor = startMonitor?.Invoke(Poll);
        }
        catch { Dispose(); throw; }
    }

    public void Play()
    {
        CheckOwner();
        ObjectDisposedException.ThrowIf(disposed, this);
        player!.Play();
        PlaybackState = PlaybackState.Playing;
    }

    public void Stop()
    {
        CheckOwner();
        PlaybackState = PlaybackState.Stopped;
        player?.Stop();
    }

    public long GetPlayedFrames()
    {
        CheckOwner();
        if (estimated && !disposed)
        {
            var produced = Math.Max(0, renderedFrames?.Invoke() ?? 0);
            var now = getTimestamp();
            if (produced != lastProduced)
            {
                lastProduced = produced;
                positionAnchor = Math.Max(playedFrames, produced - latencyFrames);
                timeAnchor = now;
            }
            var elapsed = Math.Max(0, now - timeAnchor) / (double)timestampFrequency;
            playedFrames = Math.Max(playedFrames, Math.Min(produced, positionAnchor + (long)(elapsed * sampleRate)));
        }
        else if (player is IWavePosition position && !disposed)
            playedFrames = Math.Max(playedFrames, Math.Max(0, position.GetPosition() - originBytes) / bytesPerFrame);
        return playedFrames;
    }

    public void Poll()
    {
        if (disposed) return;
        CheckOwner();
        try
        {
            var stopped = Interlocked.Exchange(ref pendingStop, null);
            if (PlaybackState == PlaybackState.Playing && stopped is null)
            {
                if (Volatile.Read(ref reset) != 0 || (resetRequested?.Invoke() ?? false))
                    stopped = new StoppedEventArgs(new InvalidOperationException("Audio driver reset; press Play to reopen output."));
                else if (Volatile.Read(ref renderFault) != 0 || (faulted?.Invoke() ?? false))
                    stopped = new StoppedEventArgs(new InvalidOperationException("Audio rendering failed."));
                else if ((endOfStream?.Invoke() ?? false) && GetPlayedFrames() >= (renderedFrames?.Invoke() ?? long.MaxValue))
                    stopped = new StoppedEventArgs();
            }
            if (stopped is null) return;
            Complete(stopped);
        }
        catch (Exception failure)
        {
            if (!disposed) Complete(new StoppedEventArgs(failure));
        }
    }

    private void Complete(StoppedEventArgs args)
    {
        PlaybackState = PlaybackState.Stopped;
        try { player?.Stop(); }
        catch (Exception error) { args = new StoppedEventArgs(args.Exception ?? error); }
        Interlocked.Exchange(ref pendingStop, null);
        PlaybackStopped?.Invoke(this, args);
    }

    private void OnReset(object? sender, EventArgs args) => Volatile.Write(ref reset, 1);

    private void OnStopped(object? sender, StoppedEventArgs args) => Interlocked.Exchange(ref pendingStop, args);

    public void Dispose()
    {
        if (disposed) return;
        CheckOwner();
        disposed = true;
        monitor?.Dispose();
        monitor = null;
        var retired = player;
        player = null;
        if (retired is not null)
        {
            retired.PlaybackStopped -= OnStopped;
            if (retired is AsioOut asio) asio.DriverResetRequest -= OnReset;
            try { retired.Stop(); }
            finally { retired.Dispose(); }
        }
        PlaybackState = PlaybackState.Stopped;
        Interlocked.Exchange(ref pendingStop, null);
    }

    private void CheckOwner()
    {
        if (startMonitor is not null && ownerThread != 0 && ownerThread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Audio output must be controlled on its creating thread.");
    }
}
