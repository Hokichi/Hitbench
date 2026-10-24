using NAudio.Wave;

namespace Hitbench.Test.Services;

public sealed class TestSampleProvider(float[] samples, int sampleRate, int channels) : ISampleProvider
{
    private int position;
    public bool FailRead { get; set; }
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    public int Read(float[] buffer, int offset, int count)
    {
        if (FailRead) throw new IOException("Sample source failed");
        var available = Math.Min(count, samples.Length - position);
        Array.Copy(samples, position, buffer, offset, available);
        position += available;
        return available;
    }
}
