namespace Hitbench.Services;

/// <summary>Frame-rate projection of observed audio time, bounded when output stops reporting progress.</summary>
public sealed class PlaybackPresentationClock
{
    private static readonly TimeSpan Horizon = TimeSpan.FromMilliseconds(100);
    private bool initialized, progressing;
    private long revision;
    private TimeSpan raw, frame, anchor, anchorFrame;

    public void Reset() { initialized = false; progressing = false; }

    public TimeSpan GetPosition(PlaybackPresentationSnapshot snapshot, TimeSpan frameTime, TimeSpan duration)
    {
        var observed = TimeSpan.FromTicks(Math.Clamp(snapshot.Playback.Position.Ticks, 0, Math.Max(0, duration.Ticks)));
        if (!initialized || revision != snapshot.Revision || frameTime < frame || !snapshot.Playback.IsPlaying || snapshot.Playback.Error is not null)
        {
            initialized = true; progressing = false; revision = snapshot.Revision;
            raw = anchor = observed; frame = anchorFrame = frameTime;
            return observed;
        }
        var predicted = progressing ? anchor + (frameTime - anchorFrame) : raw;
        if (observed > raw)
        {
            if (!progressing || (observed - predicted).Duration() > Horizon) predicted = observed;
            else if (observed > predicted) predicted = observed;
            raw = observed; anchor = predicted; anchorFrame = frameTime; progressing = true;
        }
        frame = frameTime;
        var ticks = Math.Clamp(predicted.Ticks, 0, Math.Max(0, duration.Ticks));
        return TimeSpan.FromTicks(Math.Min(ticks, raw.Ticks + Horizon.Ticks));
    }
}
