using System.IO;
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
        lock (sync)
        {
            ThrowIfDisposed();
            ClosePlayback();
            audioPath = path;
            error = null;
            currentPosition = TimeSpan.Zero;
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
                EnsureStream();
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
            if (player is null) return;
            currentPosition = GetCurrentPosition();
            player.Pause();
        }
    }

    public void Stop()
    {
        lock (sync)
        {
            ThrowIfDisposed();
            if (player is not null)
            {
                stopping = true;
                try { player.Stop(); }
                finally { stopping = false; }
            }
            if (stream is not null) stream.Position = 0;
            currentPosition = TimeSpan.Zero;
            playbackOrigin = TimeSpan.Zero;
            deviceOriginBytes = player is IWavePosition wavePosition ? wavePosition.GetPosition() : 0;
        }
    }

    public void Seek(TimeSpan position)
    {
        lock (sync)
        {
            ThrowIfDisposed();
            if (audioPath is null) return;
            try
            {
                EnsureStream();
                var audioStream = stream!;
                var wasPlaying = player?.PlaybackState == PlaybackState.Playing;
                TearDownOutput();
                var clamped = position < TimeSpan.Zero ? TimeSpan.Zero : position > audioStream.TotalTime ? audioStream.TotalTime : position;
                var bytes = (long)(clamped.TotalSeconds * audioStream.WaveFormat.AverageBytesPerSecond);
                bytes -= bytes % audioStream.WaveFormat.BlockAlign;
                audioStream.Position = Math.Clamp(bytes, 0, audioStream.Length);
                currentPosition = audioStream.CurrentTime;
                playbackOrigin = currentPosition;
                deviceOriginBytes = 0;
                if (wasPlaying)
                {
                    EnsurePlayback();
                    player!.Play();
                }
                error = null;
            }
            catch (Exception failure) when (failure is IOException or InvalidOperationException or ArgumentException)
            {
                error = $"Unable to seek audio: {failure.Message}";
                TearDownOutput();
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
        player = createPlayer();
        var currentGeneration = ++generation;
        playbackStoppedHandler = (_, args) => OnPlaybackStopped(currentGeneration, args);
        player.PlaybackStopped += playbackStoppedHandler;
        player.Init(stream);
        playbackOrigin = currentPosition;
        deviceOriginBytes = player is IWavePosition wavePosition ? wavePosition.GetPosition() : 0;
    }

    private void EnsureStream()
    {
        if (stream is not null) return;
        stream = openDecoder(audioPath!);
        stream.Position = 0;
        currentPosition = TimeSpan.Zero;
    }

    private void OnPlaybackStopped(int eventGeneration, StoppedEventArgs args)
    {
        lock (sync)
        {
            if (eventGeneration != generation || stopping || disposed) return;
            if (args.Exception is not null) error = $"Audio playback failed: {args.Exception.Message}";
            if (stream is not null) stream.Position = 0;
            currentPosition = TimeSpan.Zero;
            playbackOrigin = TimeSpan.Zero;
            deviceOriginBytes = player is IWavePosition wavePosition ? wavePosition.GetPosition() : 0;
        }
    }

    private void ClosePlayback()
    {
        TearDownOutput();
        stream?.Dispose();
        stream = null;
        currentPosition = TimeSpan.Zero;
        playbackOrigin = TimeSpan.Zero;
        deviceOriginBytes = 0;
    }

    private void TearDownOutput()
    {
        generation++;
        if (player is not null)
        {
            if (playbackStoppedHandler is not null) player.PlaybackStopped -= playbackStoppedHandler;
            try { player.Stop(); } catch { }
            player.Dispose();
            player = null;
        }
        playbackStoppedHandler = null;
    }

    private TimeSpan GetCurrentPosition()
    {
        var position = currentPosition;
        if (player is IWavePosition wavePosition && stream is not null && player.PlaybackState != PlaybackState.Stopped)
        {
            var played = Math.Max(0, wavePosition.GetPosition() - deviceOriginBytes);
            position = playbackOrigin + TimeSpan.FromSeconds(played / (double)stream.WaveFormat.AverageBytesPerSecond);
        }
        if (stream is not null && position > stream.TotalTime) position = stream.TotalTime;
        return position < TimeSpan.Zero ? TimeSpan.Zero : position;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
