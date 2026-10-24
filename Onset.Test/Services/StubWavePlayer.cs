using NAudio.Wave;

namespace Onset.Test.Services;

public sealed class StubWavePlayer : IWavePlayer, IWavePosition
{
    private EventHandler<StoppedEventArgs>? playbackStopped;
    private EventHandler<StoppedEventArgs>? retiredPlaybackStopped;

    public event EventHandler<StoppedEventArgs>? PlaybackStopped
    {
        add => playbackStopped += value;
        remove
        {
            retiredPlaybackStopped += value;
            playbackStopped -= value;
        }
    }
    public PlaybackState PlaybackState { get; private set; }
    public WaveFormat OutputWaveFormat { get; private set; } = WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2);
    public float Volume { get; set; } = 1;
    public IWaveProvider? Provider { get; private set; }
    public bool IsDisposed { get; private set; }
    public bool FailInitialization { get; set; }
    public bool FailPlayback { get; set; }
    public bool FailPosition { get; set; }
    public Action? Disposing { get; set; }
    public bool HasPlaybackStoppedSubscribers => playbackStopped is not null;
    public long Position { get; set; }
    public Func<long>? PositionReader { get; set; }
    public WaveFormat? ConfiguredOutputFormat { get; set; }
    public Action<IWaveProvider>? Initializing { get; set; }
    public Action? Playing { get; set; }

    public void Init(IWaveProvider waveProvider)
    {
        if (FailInitialization) throw new InvalidOperationException("No device");
        Provider = waveProvider;
        OutputWaveFormat = ConfiguredOutputFormat ?? waveProvider.WaveFormat;
        Initializing?.Invoke(waveProvider);
    }

    public void Play()
    {
        Playing?.Invoke();
        if (FailPlayback) throw new InvalidOperationException("Cannot start device");
        PlaybackState = PlaybackState.Playing;
    }
    public void Pause() => PlaybackState = PlaybackState.Paused;
    public void Stop()
    {
        PlaybackState = PlaybackState.Stopped;
        playbackStopped?.Invoke(this, new StoppedEventArgs());
    }

    public void RaiseStopped(Exception? error = null)
    {
        PlaybackState = PlaybackState.Stopped;
        playbackStopped?.Invoke(this, new StoppedEventArgs(error));
    }

    public void RaiseRetiredStopped(Exception? error = null) =>
        retiredPlaybackStopped?.Invoke(this, new StoppedEventArgs(error));

    public long GetPosition() => FailPosition ? throw new IOException("Position unavailable") : PositionReader?.Invoke() ?? Position;

    public void Dispose()
    {
        Disposing?.Invoke();
        IsDisposed = true;
    }
}
