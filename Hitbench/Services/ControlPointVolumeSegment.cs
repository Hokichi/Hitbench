namespace Hitbench.Services;

public sealed record ControlPointVolumeSegment(int StartTimestamp, int? EndTimestamp, long StartFrame,
    long EndFrame, int StartVolume, int EndVolume, bool IsRamp)
{
    public double EvaluateGain(long frame)
    {
        if (!IsRamp) return StartVolume / 100d;
        if (EndFrame <= StartFrame) return EndVolume / 100d;
        var progress = Math.Clamp(((double)frame - StartFrame) / ((double)EndFrame - StartFrame), 0, 1);
        return (StartVolume + (EndVolume - StartVolume) * progress) / 100d;
    }
}
