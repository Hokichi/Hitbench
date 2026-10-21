using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace osuHitsounder.Services;

public sealed class SamplePreviewService(Func<string, WaveStream> openDecoder,
    IReadOnlyList<AudioOutputCandidate> outputCandidates, Ui.OHsAudioOperationProgress? operationProgress = null) : ISamplePreviewService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private WaveStream? reader;
    private IAudioOutput? output;
    private EventHandler<StoppedEventArgs>? stoppedHandler;
    private StoppedEventArgs? stopped;
    private string? error;
    private string? warning;
    private bool disposed;
    private string? playingPath;
    private int playingVolume;
    private TimeSpan position, duration, origin;

    public Task PlayAsync(string path, int applicationVolume, CancellationToken token, TimeSpan startPosition = default) =>
        PrepareAsync(() => Start(path, applicationVolume, startPosition, token), token);

    private bool Start(string path, int applicationVolume, TimeSpan startPosition, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Retire();
        error = null;
        warning = null;
        position = duration = TimeSpan.Zero;
        var failures = new List<string>();
        foreach (var candidate in outputCandidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                reader = openDecoder(path);
                duration = reader.TotalTime;
                var bounded = TimeSpan.FromTicks(Math.Clamp(startPosition.Ticks, 0, duration.Ticks));
                if (bounded > TimeSpan.Zero) reader.CurrentTime = bounded;
                origin = position = reader.CurrentTime;
                if (reader is OHsMp3WaveStream mp3 && bounded < duration)
                {
                    // Populate a forward seek on the preparation worker, before the output callback can read it.
                    token.ThrowIfCancellationRequested();
                    mp3.PreparePosition(token);
                    reader.CurrentTime = bounded;
                    token.ThrowIfCancellationRequested();
                }
                playingPath = path;
                playingVolume = applicationVolume;
                var source = new VolumeSampleProvider(reader.ToSampleProvider()) { Volume = Math.Clamp(applicationVolume, 0, 100) / 100f };
                var next = candidate.Create();
                output = next;
                stoppedHandler = (_, args) => { if (ReferenceEquals(output, next)) Interlocked.Exchange(ref stopped, args); };
                next.PlaybackStopped += stoppedHandler;
                next.Init(source);
                token.ThrowIfCancellationRequested();
                next.Play();
                warning = failures.Count == 0 ? next.Warning : $"Using {candidate.Name}: {string.Join("; ", failures)}";
                return true;
            }
            catch (OperationCanceledException) { Retire(); throw; }
            catch (Exception failure) { failures.Add($"{candidate.Name}: {failure.Message}"); Retire(); }
        }
        throw new InvalidOperationException($"Unable to play sample: {string.Join("; ", failures)}");
    }

    public Task SeekAsync(TimeSpan position, CancellationToken token) => PrepareAsync(() =>
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return output is null || playingPath is null || Start(playingPath, playingVolume, position, token);
    }, token);

    public Task StopAsync(CancellationToken token) => RunAsync(() =>
    {
        Retire(); position = duration = TimeSpan.Zero; playingPath = null; return true;
    }, token);

    public Task<SamplePreviewSnapshot> GetSnapshotAsync(CancellationToken token) => RunAsync(() =>
    {
        if (disposed) return new SamplePreviewSnapshot(false, error, warning, position, duration);
        try { output?.Poll(); }
        catch (Exception failure) { Interlocked.Exchange(ref stopped, new StoppedEventArgs(failure)); }
        if (Interlocked.Exchange(ref stopped, null) is { } result)
        {
            error = result.Exception?.Message;
            if (error is null) position = duration;
            Retire();
        }
        if (output is not null && reader is not null)
            position = TimeSpan.FromTicks(Math.Clamp((origin + TimeSpan.FromSeconds(
                output.GetPlayedFrames() / (double)reader.WaveFormat.SampleRate)).Ticks, 0, duration.Ticks));
        return new SamplePreviewSnapshot(output?.PlaybackState == PlaybackState.Playing, error, warning, position, duration);
    }, token);

    private Task<bool> PrepareAsync(Func<bool> operation, CancellationToken token) => operationProgress is null
        ? RunAsync(operation, token) : operationProgress.RunAsync("Preparing preview...", t => RunAsync(operation, t), token);

    private async Task<T> RunAsync<T>(Func<T> operation, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(operation, token).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private void Retire()
    {
        var retired = output;
        output = null;
        try
        {
            if (retired is not null)
            {
                retired.PlaybackStopped -= stoppedHandler;
                try { retired.Stop(); }
                finally { retired.Dispose(); }
            }
        }
        finally
        {
            stoppedHandler = null;
            Interlocked.Exchange(ref stopped, null);
            reader?.Dispose();
            reader = null;
        }
    }

    public async ValueTask DisposeAsync() => await RunAsync(() =>
    {
        if (!disposed) { disposed = true; Retire(); }
        return true;
    }, CancellationToken.None).ConfigureAwait(false);
}
