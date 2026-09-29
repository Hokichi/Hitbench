using NAudio.Wave;
using osuHitsounder.Services.Ui;

namespace osuHitsounder.Services;

public sealed class AudioOutputFactory
{
    private readonly Func<string, IWavePlayer> createAsio;
    private readonly Func<IWavePlayer> createWave;
    private readonly Func<Action, IDisposable>? startMonitor;

    public AudioOutputFactory() : this(name => new AsioOut(name), WorkstationAudioService.CreateOutput,
        poll => new AudioOutputMonitor(poll)) { }

    public AudioOutputFactory(Func<string, IWavePlayer> createAsio, Func<IWavePlayer> createWave,
        Func<Action, IDisposable>? startMonitor = null)
    {
        this.createAsio = createAsio;
        this.createWave = createWave;
        this.startMonitor = startMonitor;
    }

    public IAudioOutput Create(string? asioDriverName) => new AudioOutputDevice(
        string.IsNullOrWhiteSpace(asioDriverName) ? createWave : () => createAsio(asioDriverName), startMonitor);
}
