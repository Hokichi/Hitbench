using NAudio.Wave;

namespace Soundie.Test.Services;

public sealed class NonSeekingWaveStream : WaveStream
{
    public override WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
    public override long Length => 17640;
    public override long Position { get => 0; set => throw new NotSupportedException("Seeking unavailable"); }
    public override int Read(byte[] buffer, int offset, int count) => 0;
}
