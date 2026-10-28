namespace Hitbench.Services;

public sealed class AudioMemoryBudget
{
    private readonly object sync = new();
    private long used, peak;
    public long MaximumBytes { get; }
    public long MaximumAssetBytes { get; }
    public long UsedBytes { get { lock (sync) return used; } }
    public long PeakBytes { get { lock (sync) return peak; } }
    public AudioMemoryBudget(long maximumBytes = 512L * 1024 * 1024, long maximumAssetBytes = 256L * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAssetBytes);
        MaximumBytes = maximumBytes; MaximumAssetBytes = maximumAssetBytes;
    }
    public AudioMemoryReservation Reserve(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        lock (sync)
        {
            if (bytes > MaximumBytes - used) throw new AudioMemoryLimitException("Audio memory budget exceeded.");
            var reservation = new AudioMemoryReservation(this, bytes);
            used += bytes; peak = Math.Max(peak, used);
            return reservation;
        }
    }
    internal void Release(long bytes) { lock (sync) used -= bytes; }
    internal void CheckAsset(long bytes)
    {
        if (bytes > MaximumAssetBytes) throw new AudioMemoryLimitException("Decoded audio exceeds the asset memory limit.");
    }
}
