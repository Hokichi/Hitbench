using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace Soundie.Services;

public sealed class AudioOutputFactory
{
    private readonly Func<string, IWavePlayer> createAsio;
    private readonly Func<IWavePlayer> createWave;
    private readonly Func<IWavePlayer> createWasapi;
    private readonly Func<Action, IDisposable>? startMonitor;
    private readonly bool ownThread;

    public AudioOutputFactory() : this(name => new AsioOut(name),
        () => new WasapiOut(AudioClientShareMode.Shared, true, 60), WorkstationAudioService.CreateOutput,
        null, true) { }

    public AudioOutputFactory(Func<string, IWavePlayer> createAsio, Func<IWavePlayer> createWasapi,
        Func<IWavePlayer> createWave, Func<Action, IDisposable>? startMonitor = null, bool ownThread = false)
    {
        this.createAsio = createAsio;
        this.createWasapi = createWasapi;
        this.createWave = createWave;
        this.startMonitor = startMonitor;
        this.ownThread = ownThread;
    }

    public IReadOnlyList<AudioOutputCandidate> CreateCandidates(string? asioDriverName)
    {
        var candidates = new List<AudioOutputCandidate>();
        if (!string.IsNullOrWhiteSpace(asioDriverName))
            candidates.Add(new("ASIO", () => new AudioOutputDevice(() => createAsio(asioDriverName), startMonitor,
                completeFromRenderedFrames: true, ownThread: ownThread)));
        candidates.Add(new("WASAPI", () => new AudioOutputDevice(createWasapi, startMonitor, ownThread: ownThread)));
        candidates.Add(new("WaveOut", () => new AudioOutputDevice(createWave, startMonitor, ownThread: ownThread)));
        return candidates.AsReadOnly();
    }
}
