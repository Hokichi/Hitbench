using Soundie.Core.Entities;

namespace Soundie.Services;

public interface IWorkstationAudioService : IDisposable
{
    bool IsPrepared { get; }
    Task<WaveformData> AnalyzeAsync(string audioPath, CancellationToken token);
    void Open(string audioPath);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);
    void SetSongVolume(int volume);
    void SetSamplesVolume(int volume);
    void SetApplicationVolume(int volume);
    void SetControlPoints(IReadOnlyList<ControlPointVolumeSnapshot> points);
    Task UpdateSamplesAsync(string projectPath, IReadOnlyList<SamplePlaybackEvent> events, CancellationToken token, bool forceReload = false);
    AudioPlaybackSnapshot GetSnapshot();
}
