using NAudio.Wave;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public sealed class StubAudioOutput : IAudioOutput
{
    public ISampleProvider? Source { get; private set; }
    public bool FailInitialization { get; set; }
    public bool IsDisposed { get; private set; }
    public PlaybackState PlaybackState { get; private set; }
    public string? Warning => null;
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;
    private EventHandler<StoppedEventArgs>? retired;
    private StoppedEventArgs? stopped;
    public void Init(ISampleProvider source, Func<long>? renderedFrames = null, Func<bool>? endOfStream = null, Func<bool>? faulted = null)
    {
        if (FailInitialization) throw new IOException("No output");
        Source = source;
    }
    public void Play() => PlaybackState = PlaybackState.Playing;
    public void Stop() => PlaybackState = PlaybackState.Stopped;
    public void Complete(Exception? error = null) => stopped = new(error);
    public void Poll()
    {
        if (stopped is not { } result) return;
        stopped = null;
        PlaybackState = PlaybackState.Stopped;
        PlaybackStopped?.Invoke(this, result);
    }
    public void RaiseRetiredStopped() => retired?.Invoke(this, new StoppedEventArgs());
    public long PlayedFrames { get; set; }
    public long GetPlayedFrames() => PlayedFrames;
    public void Dispose() { retired = PlaybackStopped; IsDisposed = true; }
}
