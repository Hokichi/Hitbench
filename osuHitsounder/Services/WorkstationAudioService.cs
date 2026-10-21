using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services;

public sealed class WorkstationAudioService : IWorkstationAudioService
{
    private readonly Ui.OHsAudioOperationProgress? operationProgress;
    private const int OutputSampleRate = 44_100;
    private readonly Func<string, WaveStream> openDecoder;
    private readonly AudioOutputCandidate[] outputCandidates;
    private readonly List<string> outputFailures = [];
    private int selectedOutput;
    private int activeOutput;
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
    private ControlPointVolumeTimeline controlPoints = ControlPointVolumeTimeline.Empty;
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
    public bool IsPrepared { get { lock (sync) return !disposed && preparedSong is not null; } }

    public WorkstationAudioService() : this(AudioDecoder.Open, new AudioOutputFactory().CreateCandidates(null)) { }

    internal static IWavePlayer CreateOutput() => new WaveOutEvent { DesiredLatency = 60, NumberOfBuffers = 3 };

    public WorkstationAudioService(Func<string, WaveStream> openDecoder, Func<IWavePlayer> createPlayer)
        : this(openDecoder, () => new AudioOutputDevice(createPlayer)) { }

    public WorkstationAudioService(Func<string, WaveStream> openDecoder, Func<IAudioOutput> createPlayer, Func<IAudioOutput>? createFallback = null)
        : this(openDecoder, createFallback is null
            ? [new AudioOutputCandidate("Preferred", createPlayer)]
            : [new AudioOutputCandidate("Preferred", createPlayer), new AudioOutputCandidate("Fallback", createFallback)]) { }

    public WorkstationAudioService(Func<string, WaveStream> openDecoder, IReadOnlyList<AudioOutputCandidate> outputCandidates, Ui.OHsAudioOperationProgress? operationProgress = null)
    {
        this.operationProgress = operationProgress;
        ArgumentNullException.ThrowIfNull(openDecoder);
        ArgumentNullException.ThrowIfNull(outputCandidates);
        this.outputCandidates = outputCandidates.ToArray();
        if (this.outputCandidates.Length == 0 || this.outputCandidates.Any(x => x is null || string.IsNullOrWhiteSpace(x.Name) || x.Create is null))
            throw new ArgumentException("At least one named audio output is required.", nameof(outputCandidates));
        this.openDecoder = openDecoder;
    }

    public Task<WaveformData> AnalyzeAsync(string path, CancellationToken token) => operationProgress is null
        ? AnalyzeCoreAsync(path, token) : operationProgress.RunAsync("Loading audio...", t => AnalyzeCoreAsync(path, t), token);

    private Task<WaveformData> AnalyzeCoreAsync(string path, CancellationToken token) => Task.Run(() =>
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
            controlPoints = ControlPointVolumeTimeline.Empty;
            sampleCache.Clear();
            preparationGeneration++;
            error = null;
            playbackError = null;
            warning = null;
            selectedOutput = 0;
            outputFailures.Clear();
            currentPosition = TimeSpan.Zero;
        }
        DisposeOutput(retired);
    }

    public Task UpdateSamplesAsync(string projectPath, IReadOnlyList<SamplePlaybackEvent> events, CancellationToken token, bool forceReload = false) =>
        operationProgress is null ? UpdateSamplesCoreAsync(projectPath, events, token, forceReload)
            : operationProgress.RunAsync("Preparing audio...", t => UpdateSamplesCoreAsync(projectPath, events, t, forceReload), token);

    private async Task UpdateSamplesCoreAsync(string projectPath, IReadOnlyList<SamplePlaybackEvent> events,
        CancellationToken token, bool forceReload = false)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(events);
        events = events.ToArray();
        string songPath;
        int request;
        lock (sync)
        {
            ThrowIfDisposed();
            songPath = audioPath ?? throw new InvalidOperationException("Open a song before preparing playback.");
            request = ++preparationGeneration;
            if (events.All(item => item.Id > 0) && events.Select(item => item.Id).Distinct().Count() == events.Count)
            {
                var requestedIds = events.Select(item => item.Id).ToHashSet();
                // Remove voices immediately; decoding the remaining files may take time.
                mixer?.Publish(new PreparedPlaybackSchedule(preparedEvents.Events.ToArray()
                    .Where(item => requestedIds.Contains(item.Id)).ToArray()));
            }
        }

        var acquired = false;
        try
        {
            await preparationGate.WaitAsync(token);
            acquired = true;
            token.ThrowIfCancellationRequested();
            DecodedAudio? song;
            lock (sync)
            {
                if (disposed || request != preparationGeneration) return;
                if (forceReload) sampleCache.Clear();
                song = preparedSong;
            }
            if (song is null) song = await Task.Run(() => Decode(songPath, token), token);

            if (events.Any(item => item.Id <= 0) || events.Select(item => item.Id).Distinct().Count() != events.Count)
                throw new ArgumentException("Invalid or duplicate sample placement identity.");
            var errors = new List<string>();
            var prepared = new List<PreparedSampleEvent>(events.Count);
            var referencedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resolved = new Dictionary<string, (DecodedAudio? Audio, string? Error)>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in events)
            {
                token.ThrowIfCancellationRequested();
                var fileKey = item.FileName ?? string.Empty;
                if (!resolved.TryGetValue(fileKey, out var result))
                {
                    if (!TryResolveSamplePath(projectPath, fileKey, out var samplePath))
                    {
                        resolved[fileKey] = (null, $"Unable to load sample '{item.FileName}': invalid file name.");
                        errors.Add(resolved[fileKey].Error!);
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
                        result = (cached.Samples, null);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception failure)
                    {
                        result = (null, $"Unable to load sample '{item.FileName}': {failure.Message}");
                    }
                    resolved[fileKey] = result;
                }
                if (result.Error is not null) { errors.Add(result.Error); continue; }
                var gain = Math.Clamp(item.TrackVolume, 0, 100) / 100f;
                prepared.Add(new PreparedSampleEvent(item.Id, result.Audio!, gain,
                    checked((long)Math.Round(item.Timestamp * 44_100m / 1000m, MidpointRounding.AwayFromZero))));
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
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            lock (sync)
                if (request == preparationGeneration) AbandonPreparation();
            throw;
        }
        catch (Exception failure)
        {
            lock (sync)
            {
                if (!disposed && request == preparationGeneration)
                {
                    AbandonPreparation();
                    error = $"Unable to prepare audio: {failure.Message}";
                }
            }
        }
        finally { if (acquired) preparationGate.Release(); }
    }

    public void SetControlPoints(IReadOnlyList<ControlPointVolumeSnapshot> points)
    {
        var timeline = new ControlPointVolumeTimeline(points);
        lock (sync)
        {
            ThrowIfDisposed();
            controlPoints = timeline;
            mixer?.SetControlPoints(timeline);
        }
    }

    private void AbandonPreparation() => mixer?.Publish(preparedEvents);

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
        int request;
        int first;
        lock (sync)
        {
            ThrowIfDisposed();
            if (audioPath is null) return;
            if (preparedSong is null)
            {
                playbackError = "Audio must be prepared before playback.";
                return;
            }
            if (restartAtEnd && currentPosition >= GetDuration()) ResetPosition();
            request = generation;
            first = selectedOutput;
        }
        for (var index = first; index < outputCandidates.Length; index++)
        {
            lock (sync)
            {
                if (disposed || generation != request) return;
            }
            IAudioOutput? retired;
            try
            {
                var active = EnsurePlayback(index, request);
                if (active is null) return;
                active.Play();
                lock (sync)
                {
                    if (disposed || generation != request || !ReferenceEquals(active, player)) return;
                    selectedOutput = index;
                    playbackError = null;
                    warning = index == 0 ? null : $"Using {outputCandidates[index].Name} because preferred audio output failed. {string.Join(" ", outputFailures)}";
                }
                return;
            }
            catch (Exception failure)
            {
                lock (sync)
                {
                    if (disposed || generation != request) return;
                    RecordOutputFailure(index, failure);
                    playbackError = $"Unable to play audio. {string.Join(" ", outputFailures)}";
                    retired = DetachPlayback();
                    request = generation;
                    selectedOutput = Math.Min(index + 1, outputCandidates.Length - 1);
                }
            }
            DisposeOutput(retired);
        }
    }

    private void RecordOutputFailure(int index, Exception failure)
    {
        var prefix = outputCandidates[index].Name + ": ";
        outputFailures.RemoveAll(message => message.StartsWith(prefix, StringComparison.Ordinal));
        outputFailures.Add(prefix + failure.Message);
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
            controlPoints = ControlPointVolumeTimeline.Empty;
            sampleCache.Clear();
        }
        DisposeOutput(retired);
    }

    private IAudioOutput? EnsurePlayback(int candidateIndex, int request)
    {
        ScheduledSampleMixer sessionMixer;
        long startFrame;
        int sessionGeneration;
        lock (sync)
        {
            if (disposed || generation != request) return null;
            if (player is not null) return player;
            ThrowIfDisposed();
            startFrame = TimeToFrame(currentPosition);
            sessionMixer = new ScheduledSampleMixer(preparedSong!, startFrame);
            sessionMixer.SetControlPoints(controlPoints);
            sessionMixer.SetVolumes(songVolume, samplesVolume, applicationVolume);
            sessionMixer.Publish(preparedEvents);
            sessionGeneration = generation;
        }
        var output = outputCandidates[candidateIndex].Create();
        EventHandler<StoppedEventArgs> stopped = (_, args) => OnPlaybackStopped(sessionGeneration, args);
        output.PlaybackStopped += stopped;
        try
        {
            bool superseded;
            lock (sync) superseded = disposed || generation != sessionGeneration;
            if (superseded)
            {
                output.PlaybackStopped -= stopped;
                output.Dispose();
                return null;
            }
            output.Init(sessionMixer, () => sessionMixer.RenderedFrames - startFrame,
                () => sessionMixer.EndOfStream, () => sessionMixer.Faulted);
        }
        catch
        {
            output.PlaybackStopped -= stopped;
            output.Dispose();
            throw;
        }
        lock (sync)
        {
            if (!disposed && generation == sessionGeneration)
            {
                sessionMixer.SetVolumes(songVolume, samplesVolume, applicationVolume);
                sessionMixer.SetControlPoints(controlPoints);
                sessionMixer.Publish(preparedEvents);
                mixer = sessionMixer;
                player = output;
                activeOutput = candidateIndex;
                playbackStoppedHandler = stopped;
                playbackOrigin = FrameToTime(startFrame);
                return output;
            }
        }
        output.PlaybackStopped -= stopped;
        output.Dispose();
        return null;
    }

    private void OnPlaybackStopped(int eventGeneration, StoppedEventArgs args)
    {
        IAudioOutput? completedPlayer;
        lock (sync)
        {
            if (eventGeneration != generation || disposed) return;
            if (args.Exception is not null)
            {
                playbackError = $"Audio playback failed ({outputCandidates[activeOutput].Name}): {args.Exception.Message}";
                RecordOutputFailure(activeOutput, args.Exception);
                selectedOutput = Math.Min(activeOutput + 1, outputCandidates.Length - 1);
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

    private DecodedAudio Decode(string path, CancellationToken token) => AudioDecoder.Decode(path, token, openDecoder);

    private static bool TryResolveSamplePath(string projectPath, string fileName, out string path)
    {
        path = string.Empty;
        try { path = FileSystem.SamplePaths.Resolve(projectPath, fileName); return true; }
        catch (ArgumentException) { return false; }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
