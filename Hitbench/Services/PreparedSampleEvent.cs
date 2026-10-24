namespace Hitbench.Services;

public sealed record PreparedSampleEvent(long Id, DecodedAudio Audio, float Gain, long StartFrame = 0)
{
    public long EndFrame => checked(StartFrame + Audio.FrameCount);
}
