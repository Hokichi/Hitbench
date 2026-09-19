using NAudio.Wave;
using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services;

public sealed class WorkstationAudioService : IWorkstationAudioService
{
    private readonly Func<string, WaveStream> openDecoder;
    private readonly Func<IWavePlayer> createPlayer;
    private readonly object sync = new();
    private WaveStream? stream;
    private IWavePlayer? player;
    private EventHandler<StoppedEventArgs>? playbackStoppedHandler;
    private string? audioPath;
    private string? error;
    private int generation;
    private bool stopping;
    private bool disposed;

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
        lock (sync)
        {
            ThrowIfDisposed();
            ClosePlayback();
            audioPath = path;
            error = null;
        }
    }

    public void Play()
    {
        lock (sync)
        {
            ThrowIfDisposed();
            if (audioPath is null) return;
            try
            {
                EnsurePlayback();
                player!.Play();
                error = null;
            }
            catch (Exception failure)
            {
                error = $"Unable to play audio: {failure.Message}";
                ClosePlayback();
            }
        }
    }

    public void Pause()
    {
        lock (sync)
        {
            ThrowIfDisposed();
            player?.Pause();
        }
    }

    public void Stop()
    {
        lock (sync)
        {
            ThrowIfDisposed();
            if (player is null) return;
            stopping = true;
            try { player.Stop(); }
            finally
            {
                stopping = false;
                if (stream is not null) stream.Position = 0;
            }
        }
    }

    public AudioPlaybackSnapshot GetSnapshot()
    {
        lock (sync)
        {
            var playing = player?.PlaybackState == PlaybackState.Playing;
            var position = player is IWavePosition wavePosition && stream is not null && player.PlaybackState != PlaybackState.Stopped
                ? TimeSpan.FromSeconds(wavePosition.GetPosition() / (double)stream.WaveFormat.AverageBytesPerSecond)
                : stream?.CurrentTime ?? TimeSpan.Zero;
            if (stream is not null && position > stream.TotalTime) position = stream.TotalTime;
            return new AudioPlaybackSnapshot(position, playing, error);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            ClosePlayback();
        }
    }

    private void EnsurePlayback()
    {
        if (player is not null) return;
        stream = openDecoder(audioPath!);
        player = createPlayer();
        var currentGeneration = ++generation;
        playbackStoppedHandler = (_, args) => OnPlaybackStopped(currentGeneration, args);
        player.PlaybackStopped += playbackStoppedHandler;
        player.Init(stream);
    }

    private void OnPlaybackStopped(int eventGeneration, StoppedEventArgs args)
    {
        lock (sync)
        {
            if (eventGeneration != generation || stopping || disposed) return;
            if (args.Exception is not null) error = $"Audio playback failed: {args.Exception.Message}";
            if (stream is not null) stream.Position = 0;
        }
    }

    private void ClosePlayback()
    {
        generation++;
        if (player is not null)
        {
            try { player.Stop(); } catch { }
            if (playbackStoppedHandler is not null) player.PlaybackStopped -= playbackStoppedHandler;
            player.Dispose();
            player = null;
        }
        playbackStoppedHandler = null;
        stream?.Dispose();
        stream = null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
