using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services;

public sealed class WorkstationAudioService : IWorkstationAudioService
{
    private const int OutputSampleRate = 44_100;
    private const int OutputChannels = 2;
    private static readonly WaveFormat OutputFormat = WaveFormat.CreateIeeeFloatWaveFormat(OutputSampleRate, OutputChannels);
    private readonly Func<string, WaveStream> openDecoder;
    private readonly Func<IAudioOutput> createPlayer;
    private readonly Func<IAudioOutput>? createFallback;
    private bool useFallback;
    private string? warning;
    private string? playbackError;
    private readonly object sync = new();
    private readonly SemaphoreSlim preparationGate = new(1, 1);
    private readonly Dictionary<string, (long Length, DateTime LastWrite, DecodedAudio Samples)> sampleCache =
        new(StringComparer.OrdinalIgnoreCase);
    private IAudioOutput? player;
    private ScheduledSampleMixer? mixer;
    private EventHandler<StoppedEventArgs>? playbackStoppedHandler;
    private PreparedPlaybackSchedule preparedEvents = PreparedPlaybackSchedule.Empty;
    private DecodedAudio? preparedSong;
    private string? audioPath;
    private string? error;
    private int generation;
    private int preparationGeneration;
    private bool disposed;
    private float songVolume = 1;
    private float samplesVolume = 1;
    private float applicationVolume = 1;
    private TimeSpan currentPosition;
    private TimeSpan playbackOrigin;

    public WorkstationAudioService() : this(AudioDecoder.Open, () => new AudioOutputDevice(CreateOutput, poll => new Ui.AudioOutputMonitor(poll))) { }

    internal static IWavePlayer CreateOutput() => new WaveOutEvent { DesiredLatency = 10, NumberOfBuffers = 2 };

    public WorkstationAudioService(Func<string, WaveStream> openDecoder, Func<IWavePlayer> createPlayer)
        : this(openDecoder, () => new AudioOutputDevice(createPlayer)) { }

    public WorkstationAudioService(Func<string, WaveStream> openDecoder, Func<IAudioOutput> createPlayer, Func<IAudioOutput>? createFallback = null)
    {
        this.openDecoder = openDecoder;
        this.createPlayer = createPlayer;
        this.createFallback = createFallback;
    }

    public Task<WaveformData> AnalyzeAsync(string path, CancellationToken token) => Task.Run(() =>
    {
        using var analysisStream = openDecoder(path);
        return new WaveformPeakReader().Read(analysisStream.ToSampleProvider(), token);
    }, token);

    public void Open(string path)
    {
        IAudioOutput? retired;
        lock (sync)
        {
            ThrowIfDisposed();
            retired = DetachPlayback();
            audioPath = path;
            preparedSong = null;
            preparedEvents = PreparedPlaybackSchedule.Empty;
            sampleCache.Clear();
            preparationGeneration++;
            error = null;
            playbackError = null;
            warning = null;
            useFallback = false;
            currentPosition = TimeSpan.Zero;
        }
        DisposeOutput(retired);
    }

    public async Task UpdateSamplesAsync(string projectPath, IReadOnlyList<SamplePlaybackEvent> events,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(events);
        string songPath;
        int request;
        lock (sync)
        {
            ThrowIfDisposed();
            songPath = audioPath ?? throw new InvalidOperationException("Open a song before preparing playback.");
            request = ++preparationGeneration;
        }

        events = events.ToArray();
        await preparationGate.WaitAsync(token);
        try
        {
            DecodedAudio? song;
            lock (sync) song = preparedSong;
            if (song is null) song = await Task.Run(() => Decode(songPath, token), token);

            if (events.Any(item => item.Id <= 0) || events.Select(item => item.Id).Distinct().Count() != events.Count)
                throw new ArgumentException("Invalid or duplicate sample placement identity.");
            var errors = new List<string>();
            var prepared = new List<PreparedSampleEvent>(events.Count);
            var referencedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in events)
            {
                token.ThrowIfCancellationRequested();
                if (!TryResolveSamplePath(projectPath, item.FileName, out var samplePath))
                {
                    errors.Add($"Unable to load sample '{item.FileName}': invalid file name.");
                    continue;
                }
                referencedPaths.Add(samplePath);
                try
                {
                    if (!File.Exists(samplePath)) throw new FileNotFoundException("Sample file was not found.", samplePath);
                    var info = new FileInfo(samplePath);
                    (long Length, DateTime LastWrite, DecodedAudio Samples) cached;
                    bool found;
                    lock (sync) found = sampleCache.TryGetValue(samplePath, out cached);
                    if (!found || cached.Length != info.Length ||
                        cached.LastWrite != info.LastWriteTimeUtc)
                    {
                        cached = (info.Length, info.LastWriteTimeUtc,
                            await Task.Run(() => Decode(samplePath, token), token));
                        lock (sync)
                            if (!disposed && request == preparationGeneration) sampleCache[samplePath] = cached;
                    }
                    var gain = Math.Clamp(item.BeatVolume, 0, 100) / 100f *
                        (Math.Clamp(item.TrackVolume, 0, 100) / 100f);
                    prepared.Add(new PreparedSampleEvent(item.Id, cached.Samples, gain,
                        checked((long)Math.Round(item.Timestamp * 44_100m / 1000m, MidpointRounding.AwayFromZero))));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception failure)
                {
                    errors.Add($"Unable to load sample '{item.FileName}': {failure.Message}");
                }
            }
            var schedule = new PreparedPlaybackSchedule(prepared);
            token.ThrowIfCancellationRequested();

            lock (sync)
            {
                if (disposed || request != preparationGeneration || !string.Equals(songPath, audioPath, StringComparison.Ordinal))
                    return;
                // ponytail: full-song PCM is ~20 MiB/minute; replace with bounded background decoding only if measured memory use requires it.
                preparedSong = song;
                foreach (var path in sampleCache.Keys.Where(path => !referencedPaths.Contains(path)).ToArray())
                    sampleCache.Remove(path);
                preparedEvents = schedule;
                mixer?.Publish(schedule);
                error = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors.Distinct());
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception failure)
        {
            lock (sync)
            {
                if (!disposed && request == preparationGeneration)
                    error = $"Unable to prepare audio: {failure.Message}";
            }
        }
        finally { preparationGate.Release(); }
    }

    public void SetSongVolume(int volume)
    {
        lock (sync)
        {
            ThrowIfDisposed();
            songVolume = Math.Clamp(volume, 0, 100) / 100f;
            mixer?.SetVolumes(songVolume, samplesVolume, applicationVolume);
        }
    }

    public void SetSamplesVolume(int volume)
    {
        lock (sync)
        {
            ThrowIfDisposed();
            samplesVolume = Math.Clamp(volume, 0, 100) / 100f;
            mixer?.SetVolumes(songVolume, samplesVolume, applicationVolume);
        }
    }

    public void SetApplicationVolume(int volume)
    {
        lock (sync)
        {
            ThrowIfDisposed();
            applicationVolume = Math.Clamp(volume, 0, 100) / 100f;
            mixer?.SetVolumes(songVolume, samplesVolume, applicationVolume);
        }
    }

    public void Play() => PlayCore(true);

    private void PlayCore(bool restartAtEnd)
    {
        player?.Poll();
        var fallbackAttempt = useFallback && createFallback is not null;
        useFallback = false;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            IAudioOutput? retired;
            lock (sync)
            {
                ThrowIfDisposed();
                if (audioPath is null) return;
                if (preparedSong is null)
                {
                    playbackError = "Audio must be prepared before playback.";
                    return;
                }
                try
                {
                    if (restartAtEnd && currentPosition >= GetDuration()) ResetPosition();
                    EnsurePlayback(fallbackAttempt);
                    player!.Play();
                    playbackError = null;
                    if (!fallbackAttempt) warning = null;
                    else warning ??= "ASIO unavailable; using WaveOut.";
                    return;
                }
                catch (Exception failure)
                {
                    playbackError = $"Unable to play audio{(fallbackAttempt ? " using WaveOut" : "")}: {failure.Message}";
                    retired = DetachPlayback();
                    if (!fallbackAttempt && createFallback is not null)
                        warning = $"ASIO unavailable; using WaveOut. {failure.Message}";
                }
            }
            DisposeOutput(retired);
            if (fallbackAttempt || createFallback is null) return;
            fallbackAttempt = true;
        }
    }

    public void Pause()
    {
        IAudioOutput? retired;
        lock (sync)
        {
            ThrowIfDisposed();
            if (player is null) return;
            currentPosition = GetCurrentPosition();
            retired = DetachPlayback();
        }
        DisposeOutput(retired);
    }

    public void Stop()
    {
        IAudioOutput? retired;
        lock (sync)
        {
            ThrowIfDisposed();
            retired = DetachPlayback();
            ResetPosition();
        }
        DisposeOutput(retired);
    }

    public void Seek(TimeSpan position)
    {
        IAudioOutput? retired;
        bool resume;
        int detachedGeneration;
        lock (sync)
        {
            ThrowIfDisposed();
            if (audioPath is null || preparedSong is null) return;
            resume = player?.PlaybackState == PlaybackState.Playing;
            var duration = GetDuration();
            var clamped = position < TimeSpan.Zero ? TimeSpan.Zero : position > duration ? duration : position;
            retired = DetachPlayback();
            detachedGeneration = generation;
            currentPosition = FrameToTime(TimeToFrame(clamped));
            playbackOrigin = currentPosition;
        }
        DisposeOutput(retired);
        if (resume)
        {
            bool restart;
            lock (sync) restart = !disposed && generation == detachedGeneration;
            if (restart) PlayCore(false);
        }
    }

    public AudioPlaybackSnapshot GetSnapshot()
    {
        player?.Poll();
        lock (sync)
        {
            var playing = player?.PlaybackState == PlaybackState.Playing;
            return new AudioPlaybackSnapshot(GetCurrentPosition(), playing, playbackError ?? error, warning);
        }
    }

    public void Dispose()
    {
        IAudioOutput? retired;
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            preparationGeneration++;
            retired = DetachPlayback();
            ResetPosition();
            preparedSong = null;
            preparedEvents = PreparedPlaybackSchedule.Empty;
            sampleCache.Clear();
        }
        DisposeOutput(retired);
    }

    private void EnsurePlayback(bool fallback = false)
    {
        if (player is not null) return;
        var startFrame = TimeToFrame(currentPosition);
        mixer = new ScheduledSampleMixer(preparedSong!, startFrame);
        mixer.SetVolumes(songVolume, samplesVolume, applicationVolume);
        mixer.Publish(preparedEvents);
        player = fallback ? createFallback!() : createPlayer();
        var currentGeneration = ++generation;
        playbackStoppedHandler = (_, args) => OnPlaybackStopped(currentGeneration, args);
        player.PlaybackStopped += playbackStoppedHandler;
        var sessionMixer = mixer;
        player.Init(mixer, () => sessionMixer.RenderedFrames - startFrame,
            () => sessionMixer.EndOfStream, () => sessionMixer.Faulted);
        playbackOrigin = FrameToTime(startFrame);
    }

    private void OnPlaybackStopped(int eventGeneration, StoppedEventArgs args)
    {
        IAudioOutput? completedPlayer;
        lock (sync)
        {
            if (eventGeneration != generation || disposed) return;
            if (args.Exception is not null)
            {
                playbackError = $"Audio playback failed: {args.Exception.Message}";
                useFallback = createFallback is not null;
            }
            completedPlayer = DetachPlayback();
            ResetPosition();
            if (args.Exception is null) currentPosition = GetDuration();
        }
        completedPlayer?.Dispose();
    }

    private IAudioOutput? DetachPlayback()
    {
        generation++;
        var retired = player;
        if (retired is not null && playbackStoppedHandler is not null)
            retired.PlaybackStopped -= playbackStoppedHandler;
        player = null;
        playbackStoppedHandler = null;
        mixer = null;
        return retired;
    }

    private void ResetPosition()
    {
        currentPosition = TimeSpan.Zero;
        playbackOrigin = TimeSpan.Zero;
    }

    private static void DisposeOutput(IAudioOutput? retired)
    {
        if (retired is null) return;
        try { retired.Stop(); } catch { }
        retired.Dispose();
    }

    private TimeSpan GetCurrentPosition()
    {
        var position = currentPosition;
        if (player is not null && player.PlaybackState != PlaybackState.Stopped)
            position = playbackOrigin + FrameToTime(player.GetPlayedFrames());
        var duration = GetDuration();
        if (position > duration) position = duration;
        return position < TimeSpan.Zero ? TimeSpan.Zero : position;
    }

    private TimeSpan GetDuration() => preparedSong is null
        ? TimeSpan.Zero
        : FrameToTime(preparedSong.FrameCount);

    private static long TimeToFrame(TimeSpan time) => (long)Math.Round(
        time.TotalSeconds * OutputSampleRate, MidpointRounding.AwayFromZero);

    private static TimeSpan FrameToTime(long frame) => TimeSpan.FromSeconds(frame / (double)OutputSampleRate);

    private DecodedAudio Decode(string path, CancellationToken token)
    {
        using var stream = openDecoder(path);
        ISampleProvider provider = stream.ToSampleProvider();
        provider = provider.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(provider),
            2 => provider,
            _ => throw new NotSupportedException($"Unsupported channel count: {provider.WaveFormat.Channels}")
        };
        if (provider.WaveFormat.SampleRate != OutputSampleRate)
            provider = new WdlResamplingSampleProvider(provider, OutputSampleRate);
        var result = new List<float>();
        var buffer = new float[8192];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            result.AddRange(buffer.AsSpan(0, read).ToArray());
        }
        if (result.Count == 0) throw new InvalidDataException("Audio file is empty.");
        return DecodedAudio.CopyFrom(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(result));
    }

    private static bool TryResolveSamplePath(string projectPath, string fileName, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)) return false;
        var sampleFolder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, "Samples");
        path = Path.Combine(sampleFolder, fileName);
        return true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
