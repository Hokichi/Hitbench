using NAudio.Wave;
namespace Hitbench.Services;
public sealed class CancellationWaveProvider(IWaveProvider source, CancellationToken token) : IWaveProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;
    public int Read(byte[] buffer, int offset, int count)
    {
        token.ThrowIfCancellationRequested(); var read = source.Read(buffer, offset, count);
        token.ThrowIfCancellationRequested(); return read;
    }
}
