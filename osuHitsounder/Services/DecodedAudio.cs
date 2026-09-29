namespace osuHitsounder.Services;

public sealed class DecodedAudio
{
    private readonly float[] samples;
    private DecodedAudio(float[] samples) => this.samples = samples;
    public int FrameCount => samples.Length / 2;
    public ReadOnlySpan<float> Samples => samples;
    internal ReadOnlyMemory<float> Memory => samples;

    public static DecodedAudio CopyFrom(ReadOnlySpan<float> samples)
    {
        if (samples.Length % 2 != 0) throw new ArgumentException("Stereo PCM must contain complete frames.", nameof(samples));
        return new DecodedAudio(samples.ToArray());
    }
}
