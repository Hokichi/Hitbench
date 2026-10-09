using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace osuHitsounder.Services;

public sealed class SamplePreviewService(Func<string, WaveStream> openDecoder,
    IReadOnlyList<AudioOutputCandidate> outputCandidates) : ISamplePreviewService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private WaveStream? reader;
    private IAudioOutput? output;
    private EventHandler<StoppedEventArgs>? stoppedHandler;
    private StoppedEventArgs? stopped;
    private string? error;
    private string? warning;
    private bool disposed;

    public Task PlayAsync(string path, int applicationVolume, CancellationToken token) => RunAsync(() =>
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Retire();
        error = null;
        warning = null;
        var failures = new List<string>();
        foreach (var candidate in outputCandidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                reader = openDecoder(path);
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
    }, token);

    public Task StopAsync(CancellationToken token) => RunAsync(() => { Retire(); return true; }, token);

    public Task<SamplePreviewSnapshot> GetSnapshotAsync(CancellationToken token) => RunAsync(() =>
    {
        if (disposed) return new SamplePreviewSnapshot(false, error, warning);
        try { output?.Poll(); }
        catch (Exception failure) { Interlocked.Exchange(ref stopped, new StoppedEventArgs(failure)); }
        if (Interlocked.Exchange(ref stopped, null) is { } result)
        {
            error = result.Exception?.Message;
            Retire();
        }
        return new SamplePreviewSnapshot(output?.PlaybackState == PlaybackState.Playing, error, warning);
    }, token);

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
