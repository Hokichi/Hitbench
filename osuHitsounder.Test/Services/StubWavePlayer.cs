using NAudio.Wave;

namespace osuHitsounder.Test.Services;

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
    public bool HasPlaybackStoppedSubscribers => playbackStopped is not null;
    public long Position { get; set; }

    public void Init(IWaveProvider waveProvider)
    {
        if (FailInitialization) throw new InvalidOperationException("No device");
        Provider = waveProvider;
        OutputWaveFormat = waveProvider.WaveFormat;
    }

    public void Play() => PlaybackState = PlaybackState.Playing;
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

    public long GetPosition() => Position;

    public void Dispose() => IsDisposed = true;
}
