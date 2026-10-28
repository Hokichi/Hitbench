namespace Hitbench.Services;

public sealed class DecodedAudio : IDisposable
{
    private AudioPcmBuffer? buffer;
    private AudioPcmBuffer Buffer => Volatile.Read(ref buffer) ?? throw new ObjectDisposedException(nameof(DecodedAudio));
    private DecodedAudio(AudioPcmBuffer buffer) => this.buffer = buffer;
    public int FrameCount => Buffer.Values.Length / 2;
    public ReadOnlySpan<float> Samples => Buffer.Values;
    internal AudioPcmBuffer Storage => Buffer;
    internal AudioMemoryBudget? Budget => Buffer.Budget;
    internal void AttachBudget(AudioMemoryBudget budget) => Buffer.AttachBudget(budget);
    public DecodedAudio Retain() { var current = Buffer; current.Retain(); return new(current); }
    public void Dispose() => Interlocked.Exchange(ref buffer, null)?.Release();

    public static DecodedAudio CopyFrom(ReadOnlySpan<float> samples)
    {
        if (samples.Length % 2 != 0) throw new ArgumentException("Stereo PCM must contain complete frames.", nameof(samples));
        return new DecodedAudio(new AudioPcmBuffer(samples.ToArray(), null));
    }

    public static DecodedAudio CopyFrom(ReadOnlySpan<float> samples, AudioMemoryBudget budget)
    {
        if (samples.Length % 2 != 0) throw new ArgumentException("Stereo PCM must contain complete frames.", nameof(samples));
        var bytes = checked((long)samples.Length * sizeof(float));
        budget.CheckAsset(bytes);
        var charge = budget.Reserve(bytes);
        try { return TakeOwnership(samples.ToArray(), charge); }
        catch { charge.Dispose(); throw; }
    }

    // Caller transfers an exclusively owned buffer and must never mutate it again.
    internal static DecodedAudio TakeOwnership(float[] samples)
    {
        if (samples.Length % 2 != 0) throw new ArgumentException("Stereo PCM must contain complete frames.", nameof(samples));
        return new DecodedAudio(new AudioPcmBuffer(samples, null));
    }

    internal static DecodedAudio TakeOwnership(float[] samples, AudioMemoryReservation reservation)
    {
        if (samples.Length % 2 != 0 || reservation.Bytes != (long)samples.Length * sizeof(float))
            throw new ArgumentException("PCM reservation does not match complete stereo frames.");
        return new(new AudioPcmBuffer(samples, reservation));
    }
}
