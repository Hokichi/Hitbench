namespace osuHitsounder.Services;

public sealed record PreparedSampleEvent(long Id, DecodedAudio Audio, float Gain, long StartFrame = 0)
{
    public long EndFrame => checked(StartFrame + Audio.FrameCount);
    internal ReadOnlyMemory<float> Samples => Audio.Memory;
    public PreparedSampleEvent(long id, ReadOnlyMemory<float> samples, float gain)
        : this(id, DecodedAudio.CopyFrom(samples.Span), gain) { }
}
