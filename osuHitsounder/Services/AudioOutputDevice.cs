using NAudio.Wave;

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

    public AudioOutputDevice(Func<IWavePlayer> createPlayer, Func<Action, IDisposable>? startMonitor = null)
    {
        this.createPlayer = createPlayer;
        this.startMonitor = startMonitor;
    }

    public PlaybackState PlaybackState { get; private set; }
    public string? Warning => null;
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public void Init(ISampleProvider source, Func<long>? renderedFrames = null, Func<bool>? endOfStream = null, Func<bool>? faulted = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (player is not null) throw new InvalidOperationException("Output is already initialized.");
        ownerThread = Environment.CurrentManagedThreadId;
        try
        {
            player = createPlayer();
            player.PlaybackStopped += OnStopped;
            bytesPerFrame = source.WaveFormat.Channels * sizeof(float);
            player.Init(source.ToWaveProvider());
            originBytes = player is IWavePosition position ? position.GetPosition() : 0;
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
        if (player is IWavePosition position && !disposed)
            playedFrames = Math.Max(playedFrames, Math.Max(0, position.GetPosition() - originBytes) / bytesPerFrame);
        return playedFrames;
    }

    public void Poll()
    {
        if (disposed) return;
        CheckOwner();
        var stopped = Interlocked.Exchange(ref pendingStop, null);
        if (stopped is null) return;
        PlaybackState = PlaybackState.Stopped;
        PlaybackStopped?.Invoke(this, stopped);
    }

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
