using NAudio.Wave;
using NAudio.CoreAudioApi;
using osuHitsounder.Services.Ui;

namespace osuHitsounder.Services;

public sealed class AudioOutputFactory
{
    private readonly Func<string, IWavePlayer> createAsio;
    private readonly Func<IWavePlayer> createWave;
    private readonly Func<IWavePlayer> createWasapi;
    private readonly Func<Action, IDisposable>? startMonitor;

    public AudioOutputFactory() : this(name => new AsioOut(name),
        () => new WasapiOut(AudioClientShareMode.Shared, true, 60), WorkstationAudioService.CreateOutput,
        poll => new AudioOutputMonitor(poll)) { }

    public AudioOutputFactory(Func<string, IWavePlayer> createAsio, Func<IWavePlayer> createWave,
        Func<Action, IDisposable>? startMonitor = null)
        : this(createAsio, createWave, createWave, startMonitor) { }

    public AudioOutputFactory(Func<string, IWavePlayer> createAsio, Func<IWavePlayer> createWasapi,
        Func<IWavePlayer> createWave, Func<Action, IDisposable>? startMonitor = null)
    {
        this.createAsio = createAsio;
        this.createWasapi = createWasapi;
        this.createWave = createWave;
        this.startMonitor = startMonitor;
    }

    public IAudioOutput Create(string? asioDriverName) => new AudioOutputDevice(
        string.IsNullOrWhiteSpace(asioDriverName) ? createWave : () => createAsio(asioDriverName), startMonitor);

    public IReadOnlyList<AudioOutputCandidate> CreateCandidates(string? asioDriverName)
    {
        var candidates = new List<AudioOutputCandidate>();
        if (!string.IsNullOrWhiteSpace(asioDriverName))
            candidates.Add(new("ASIO", () => new AudioOutputDevice(() => createAsio(asioDriverName), startMonitor,
                completeFromRenderedFrames: true)));
        candidates.Add(new("WASAPI", () => new AudioOutputDevice(createWasapi, startMonitor)));
        candidates.Add(new("WaveOut", () => new AudioOutputDevice(createWave, startMonitor)));
        return candidates.AsReadOnly();
    }
}
