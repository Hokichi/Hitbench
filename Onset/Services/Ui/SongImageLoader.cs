using System.IO;
using System.Windows.Media.Imaging;

namespace Onset.Services.Ui;

public sealed class SongImageLoader
{
    private const long MemoryLimit = 64 * 1024 * 1024;
    private readonly SemaphoreSlim slots = new(2);
    private readonly object gate = new();
    private readonly Dictionary<(string Path, int Width), BitmapSource> cache = [];
    private readonly Queue<(string Path, int Width)> order = [];
    private long bytes;
    private int generation;

    public async Task<BitmapSource?> LoadAsync(string path, int decodeWidth, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path)) return null;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decodeWidth);
        try
        {
            var key = (Path.GetFullPath(path).ToUpperInvariant(), decodeWidth);
            int requestGeneration;
            lock (gate)
            {
                if (cache.TryGetValue(key, out var existing)) return existing;
                requestGeneration = generation;
            }
            await slots.WaitAsync(token).ConfigureAwait(false);
            try
            {
                lock (gate)
                    if (cache.TryGetValue(key, out var existing)) return existing;
                var image = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using var stream = File.OpenRead(path);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = decodeWidth;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (gate)
                {
                    if (requestGeneration != generation) return image;
                    if (cache.TryGetValue(key, out var existing)) return existing;
                    var size = SizeOf(image);
                    while (bytes + size > MemoryLimit && order.TryDequeue(out var oldest))
                    {
                        bytes -= SizeOf(cache[oldest]);
                        cache.Remove(oldest);
                    }
                    if (size <= MemoryLimit)
                    {
                        cache.Add(key, image);
                        order.Enqueue(key);
                        bytes += size;
                    }
                }
                return image;
            }
            finally { slots.Release(); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            NotSupportedException or System.IO.FileFormatException or ArgumentException)
        {
            return null;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            generation++;
            cache.Clear();
            order.Clear();
            bytes = 0;
        }
    }

    private static long SizeOf(BitmapSource image) =>
        (long)image.PixelWidth * image.PixelHeight * ((image.Format.BitsPerPixel + 7) / 8);
}
