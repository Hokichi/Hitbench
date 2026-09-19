using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services;

public interface IWorkstationAudioService : IDisposable
{
    Task<WaveformData> AnalyzeAsync(string audioPath, CancellationToken token);
    void Open(string audioPath);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);
    AudioPlaybackSnapshot GetSnapshot();
}
