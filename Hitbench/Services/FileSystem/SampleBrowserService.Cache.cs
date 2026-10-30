using System.IO;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
namespace Hitbench.Services.FileSystem;
public sealed partial class SampleBrowserService
{
    private readonly object cacheLock = new();
    private readonly Dictionary<SampleFileStamp, SampleFileMetadata> metadataCache = [];
    private readonly Dictionary<SampleFileStamp, WaveformData> waveformCache = [];
    private readonly Dictionary<SampleFileStamp, SampleMetadataRead> metadataReads = [];
    private bool disposed;
    internal int PendingMetadataWaiterCount { get { lock (cacheLock) return metadataReads.Values.Sum(read => read.Waiters); } }
    internal int MetadataCacheCount { get { lock (cacheLock) return metadataCache.Count; } }
    internal int WaveformCacheCount { get { lock (cacheLock) return waveformCache.Count; } }
    internal long CachedWaveformBytes { get { lock (cacheLock) return waveformCache.Values.Sum(value => (long)value.Peaks.Count * sizeof(float)); } }
    private static SampleFileStamp? ReadStamp(string path)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        return file.Exists ? new(file.FullName.ToUpperInvariant(), file.Length, file.LastWriteTimeUtc) : null;
    }
    private async Task<SampleFileMetadata> ReadMetadataCachedAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var signature = await Task.Run(() => ReadStamp(path), token).ConfigureAwait(false);
        if (signature is not { } stamp) return await ReadMetadataCoreAsync(path, token).ConfigureAwait(false);
        SampleMetadataRead read;
        lock (cacheLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (metadataCache.TryGetValue(stamp, out var cached)) return cached;
            if (!metadataReads.TryGetValue(stamp, out read!))
            {
                read = new();
                read.Task = ReadMetadataCoreAsync(path, read.Cancellation.Token);
                metadataReads.Add(stamp, read);
            }
            read.Waiters++;
        }
        try
        {
            var result = await read.Task.WaitAsync(token).ConfigureAwait(false);
            if (ReadStamp(path) == stamp)
                lock (cacheLock)
                {
                    if (!disposed)
                    {
                        foreach (var old in metadataCache.Keys.Where(key => key.Path == stamp.Path && key != stamp).ToArray()) metadataCache.Remove(old);
                        while (metadataCache.Count >= 512 && !metadataCache.ContainsKey(stamp)) metadataCache.Remove(metadataCache.Keys.First());
                        metadataCache[stamp] = result;
                    }
                }
            return result;
        }
        finally
        {
            lock (cacheLock)
                if (--read.Waiters == 0)
                {
                    metadataReads.Remove(stamp);
                    read.Cancellation.Cancel();
                    _ = read.Task.ContinueWith(task => { _ = task.Exception; read.Cancellation.Dispose(); }, TaskScheduler.Default);
                }
        }
    }
    private async Task<WaveformData> ReadWaveformCachedAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var stamp = await Task.Run(() => ReadStamp(path), token).ConfigureAwait(false);
        lock (cacheLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (stamp is { } key && waveformCache.TryGetValue(key, out var cached)) return cached;
        }
        var result = await ReadWaveformCoreAsync(path, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (stamp is { } current && ReadStamp(path) == stamp && (long)result.Peaks.Count * sizeof(float) <= 8 * 1024 * 1024)
            lock (cacheLock)
            {
                if (!disposed)
                {
                    foreach (var old in waveformCache.Keys.Where(key => key.Path == current.Path).ToArray()) waveformCache.Remove(old);
                    while (waveformCache.Count > 0 && (waveformCache.Count >= 32 || CachedWaveformBytes + (long)result.Peaks.Count * sizeof(float) > 8 * 1024 * 1024)) waveformCache.Remove(waveformCache.Keys.First());
                    waveformCache[current] = result;
                }
            }
        return result;
    }
    public void Dispose()
    {
        lock (cacheLock)
        {
            disposed = true;
            foreach (var read in metadataReads.Values.ToArray()) read.Cancellation.Cancel();
            metadataCache.Clear(); waveformCache.Clear();
        }
    }
}
