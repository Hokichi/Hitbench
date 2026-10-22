using NAudio.Wave;

namespace Soundie.Services;

internal sealed class GuardedSampleProvider(ISampleProvider source, Func<bool> silence, Action fault) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        if (silence()) { buffer.AsSpan(offset, count).Clear(); return count; }
        try { return source.Read(buffer, offset, count); }
        catch
        {
            buffer.AsSpan(offset, count).Clear();
            fault();
            return count;
        }
    }
}
