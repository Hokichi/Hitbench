namespace Hitbench.Services;

public sealed class AudioMemoryReservation : IDisposable
{
    private AudioMemoryBudget? owner;
    internal AudioMemoryBudget Budget => owner ?? throw new ObjectDisposedException(nameof(AudioMemoryReservation));
    public long Bytes { get; }
    internal AudioMemoryReservation(AudioMemoryBudget owner, long bytes) { this.owner = owner; Bytes = bytes; }
    public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release(Bytes);
}
