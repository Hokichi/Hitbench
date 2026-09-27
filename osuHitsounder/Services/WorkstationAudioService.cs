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
    private readonly Func<IWavePlayer> createPlayer;
    private readonly object sync = new();
    private readonly SemaphoreSlim preparationGate = new(1, 1);
    private readonly Dictionary<string, (long Length, DateTime LastWrite, float[] Samples)> sampleCache =
        new(StringComparer.OrdinalIgnoreCase);
    private IWavePlayer? player;
    private ScheduledSampleMixer? mixer;
    private EventHandler<StoppedEventArgs>? playbackStoppedHandler;
    private PreparedSampleEvent[] preparedEvents = [];
    private float[]? preparedSong;
    private string? audioPath;
    private string? error;
    private int generation;
    private int preparationGeneration;
    private bool disposed;
    private float songVolume = 1;
    private TimeSpan currentPosition;
    private TimeSpan playbackOrigin;
    private long deviceOriginBytes;

    public WorkstationAudioService() : this(AudioDecoder.Open, () => new WaveOutEvent()) { }

    public WorkstationAudioService(Func<string, WaveStream> openDecoder, Func<IWavePlayer> createPlayer)
    {
        this.openDecoder = openDecoder;
        this.createPlayer = createPlayer;
    }

    public Task<WaveformData> AnalyzeAsync(string path, CancellationToken token) => Task.Run(() =>
    {
        using var analysisStream = openDecoder(path);
        return new WaveformPeakReader().Read(analysisStream.ToSampleProvider(), token);
    }, token);

    public void Open(string path)
    {
        IWavePlayer? retired;
        lock (sync)
        {
            ThrowIfDisposed();
            retired = DetachPlayback();
            audioPath = path;
            preparedSong = null;
            preparedEvents = [];
            sampleCache.Clear();
            preparationGeneration++;
            error = null;
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

        await preparationGate.WaitAsync(token);
        try
        {
            float[] song;
            lock (sync) song = preparedSong ?? [];
            if (song.Length == 0) song = await Task.Run(() => Decode(songPath, token), token);

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
                    if (!sampleCache.TryGetValue(samplePath, out var cached) || cached.Length != info.Length ||
                        cached.LastWrite != info.LastWriteTimeUtc)
                    {
                        cached = (info.Length, info.LastWriteTimeUtc,
                            await Task.Run(() => Decode(samplePath, token), token));
                        sampleCache[samplePath] = cached;
                    }
                    var startFrame = (long)Math.Round(item.Timestamp / 1000d * OutputSampleRate,
                        MidpointRounding.AwayFromZero);
                    var gain = Math.Clamp(item.BeatVolume, 0, 100) / 100f *
                        (Math.Clamp(item.TrackVolume, 0, 100) / 100f);
                    prepared.Add(new PreparedSampleEvent(item.Id, startFrame, cached.Samples, gain));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception failure)
                {
                    errors.Add($"Unable to load sample '{item.FileName}': {failure.Message}");
                }
            }
            foreach (var path in sampleCache.Keys.Where(path => !referencedPaths.Contains(path)).ToArray())
                sampleCache.Remove(path);

            lock (sync)
            {
                if (disposed || request != preparationGeneration || !string.Equals(songPath, audioPath, StringComparison.Ordinal))
                    return;
                // ponytail: full-song PCM is ~20 MiB/minute; replace with bounded background decoding only if measured memory use requires it.
                preparedSong = song;
                preparedEvents = prepared.ToArray();
                mixer?.ReplaceSchedule(preparedEvents);
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
            if (mixer is not null) mixer.SongVolume = songVolume;
        }
    }

    public void Play()
    {
        IWavePlayer? retired = null;
        lock (sync)
        {
            ThrowIfDisposed();
            if (audioPath is null) return;
            if (preparedSong is null)
            {
                error = "Audio must be prepared before playback.";
                return;
            }
            try
            {
                EnsurePlayback();
                player!.Play();
            }
            catch (Exception failure)
            {
                error = $"Unable to play audio: {failure.Message}";
                retired = DetachPlayback();
                ResetPosition();
            }
        }
        DisposeOutput(retired);
    }

    public void Pause()
    {
        lock (sync)
        {
            ThrowIfDisposed();
            if (player is null) return;
            currentPosition = GetCurrentPosition();
            player.Pause();
        }
    }

    public void Stop()
    {
        IWavePlayer? retired;
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
        IWavePlayer? retired;
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
            deviceOriginBytes = 0;
        }
        DisposeOutput(retired);
        if (resume)
        {
            lock (sync)
            {
                if (!disposed && generation == detachedGeneration)
                {
                    EnsurePlayback();
                    player!.Play();
                }
            }
        }
    }

    public AudioPlaybackSnapshot GetSnapshot()
    {
        lock (sync)
        {
            var playing = player?.PlaybackState == PlaybackState.Playing;
            return new AudioPlaybackSnapshot(GetCurrentPosition(), playing, error);
        }
    }

    public void Dispose()
    {
        IWavePlayer? retired;
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            preparationGeneration++;
            retired = DetachPlayback();
            ResetPosition();
            preparedSong = null;
            preparedEvents = [];
            sampleCache.Clear();
        }
        DisposeOutput(retired);
    }

    private void EnsurePlayback()
    {
        if (player is not null) return;
        var startFrame = TimeToFrame(currentPosition);
        mixer = new ScheduledSampleMixer(preparedSong!, OutputFormat, startFrame) { SongVolume = songVolume };
        mixer.ReplaceSchedule(preparedEvents);
        player = createPlayer();
        var currentGeneration = ++generation;
        playbackStoppedHandler = (_, args) => OnPlaybackStopped(currentGeneration, args);
        player.PlaybackStopped += playbackStoppedHandler;
        player.Init(mixer.ToWaveProvider());
        playbackOrigin = FrameToTime(startFrame);
        deviceOriginBytes = player is IWavePosition wavePosition ? wavePosition.GetPosition() : 0;
    }

    private void OnPlaybackStopped(int eventGeneration, StoppedEventArgs args)
    {
        IWavePlayer? completedPlayer;
        lock (sync)
        {
            if (eventGeneration != generation || disposed) return;
            if (args.Exception is not null) error = $"Audio playback failed: {args.Exception.Message}";
            completedPlayer = DetachPlayback();
            ResetPosition();
        }
        completedPlayer?.Dispose();
    }

    private IWavePlayer? DetachPlayback()
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
        deviceOriginBytes = 0;
    }

    private static void DisposeOutput(IWavePlayer? retired)
    {
        if (retired is null) return;
        try { retired.Stop(); } catch { }
        retired.Dispose();
    }

    private TimeSpan GetCurrentPosition()
    {
        var position = currentPosition;
        if (player is IWavePosition wavePosition && player.PlaybackState != PlaybackState.Stopped)
        {
            var played = Math.Max(0, wavePosition.GetPosition() - deviceOriginBytes);
            position = playbackOrigin + TimeSpan.FromSeconds(played / (double)OutputFormat.AverageBytesPerSecond);
        }
        var duration = GetDuration();
        if (position > duration) position = duration;
        return position < TimeSpan.Zero ? TimeSpan.Zero : position;
    }

    private TimeSpan GetDuration() => preparedSong is null
        ? TimeSpan.Zero
        : FrameToTime(preparedSong.Length / OutputChannels);

    private static long TimeToFrame(TimeSpan time) => (long)Math.Round(
        time.TotalSeconds * OutputSampleRate, MidpointRounding.AwayFromZero);

    private static TimeSpan FrameToTime(long frame) => TimeSpan.FromSeconds(frame / (double)OutputSampleRate);

    private float[] Decode(string path, CancellationToken token)
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
        return result.ToArray();
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
