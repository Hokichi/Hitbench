namespace Onset.Services.Ui;

internal sealed class SongScrollMotion
{
    internal bool IsActive { get; private set; }
    internal double TargetOffset { get; private set; }
    private double startOffset;
    private TimeSpan started;
    private double progress;
    internal double Request(int delta, int wheelLines, double rowStride, double viewportHeight,
        double maximumOffset, double currentOffset, TimeSpan now, bool animate)
    {
        var distance = -delta / 120d * (wheelLines < 0 ? viewportHeight : wheelLines * rowStride);
        currentOffset = Clamp(currentOffset, maximumOffset);
        var sameDirection = IsActive && Math.Sign(TargetOffset - currentOffset) == Math.Sign(distance);
        TargetOffset = Clamp((sameDirection ? TargetOffset : currentOffset) + distance, maximumOffset);
        startOffset = currentOffset;
        started = now;
        progress = 0;
        IsActive = animate && Math.Abs(TargetOffset - currentOffset) > .001;
        if (!IsActive) startOffset = TargetOffset;
        return IsActive ? currentOffset : TargetOffset;
    }
    internal double Advance(TimeSpan now, double maximumOffset)
    {
        TargetOffset = Clamp(TargetOffset, maximumOffset);
        startOffset = Clamp(startOffset, maximumOffset);
        if (!IsActive) return TargetOffset;
        progress = Math.Max(progress, Math.Clamp((now - started).TotalMilliseconds / 160, 0, 1));
        var value = startOffset + (TargetOffset - startOffset) * (1 - Math.Pow(1 - progress, 3));
        if (progress >= 1 || Math.Abs(TargetOffset - startOffset) < .001) IsActive = false;
        return value;
    }
    internal void Cancel(double currentOffset)
    {
        IsActive = false;
        startOffset = TargetOffset = Math.Max(0, double.IsFinite(currentOffset) ? currentOffset : 0);
    }
    private static double Clamp(double offset, double maximum) => Math.Clamp(offset, 0, Math.Max(0, maximum));
}
