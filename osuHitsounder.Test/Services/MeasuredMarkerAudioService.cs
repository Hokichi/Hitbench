using osuHitsounder.Core.Entities;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

/// <summary>Records explicit trigger entry while retaining the production audio/mixer path.</summary>
public sealed class MeasuredMarkerAudioService(IWorkstationAudioService inner, Action<IReadOnlyList<long>> triggering) : IWorkstationAudioService
{
    public void TriggerSamples(IReadOnlyList<long> ids) { triggering(ids); inner.TriggerSamples(ids); }
    public Task<WaveformData> AnalyzeAsync(string path, CancellationToken token) => inner.AnalyzeAsync(path, token);
    public void Open(string path) => inner.Open(path);
    public void Play() => inner.Play();
    public void Pause() => inner.Pause();
    public void Stop() => inner.Stop();
    public void Seek(TimeSpan position) => inner.Seek(position);
    public void SetSongVolume(int value) => inner.SetSongVolume(value);
    public void SetSamplesVolume(int value) => inner.SetSamplesVolume(value);
    public void SetApplicationVolume(int value) => inner.SetApplicationVolume(value);
    public Task UpdateSamplesAsync(string path, IReadOnlyList<SamplePlaybackEvent> events, CancellationToken token) => inner.UpdateSamplesAsync(path, events, token);
    public AudioPlaybackSnapshot GetSnapshot() => inner.GetSnapshot();
    public void Dispose() => inner.Dispose();
}
