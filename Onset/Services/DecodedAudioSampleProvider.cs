using NAudio.Wave;

namespace Onset.Services;

internal sealed class DecodedAudioSampleProvider(DecodedAudio audio) : ISampleProvider
{
    private int position;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    public int Read(float[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException(nameof(offset));
        var read = Math.Min(count, audio.Samples.Length - position);
        audio.Samples.Slice(position, read).CopyTo(buffer.AsSpan(offset, read));
        position += read;
        return read;
    }
}
