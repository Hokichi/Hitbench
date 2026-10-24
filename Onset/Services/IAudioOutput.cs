using NAudio.Wave;

namespace Onset.Services;

public interface IAudioOutput : IDisposable
{
    void Init(ISampleProvider source, Func<long>? renderedFrames = null, Func<bool>? endOfStream = null, Func<bool>? faulted = null);
    void Play();
    void Stop();
    void Poll();
    PlaybackState PlaybackState { get; }
    long GetPlayedFrames();
    string? Warning { get; }
    event EventHandler<StoppedEventArgs>? PlaybackStopped;
}
