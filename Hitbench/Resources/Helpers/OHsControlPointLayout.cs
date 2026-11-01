using Hitbench.Resources.DataModels;

namespace Hitbench.Resources.Helpers;

public static class OHsControlPointLayout
{
    public static IReadOnlyList<OHsControlPointCurveProjection> GetCurveProjections(
        IReadOnlyList<ControlPointSnapshot> points, double durationMilliseconds,
        double clipStartMilliseconds = 0, double? clipEndMilliseconds = null)
    {
        var clipEnd = Math.Min(durationMilliseconds, clipEndMilliseconds ?? durationMilliseconds);
        if (!double.IsFinite(durationMilliseconds) || !double.IsFinite(clipStartMilliseconds) ||
            !double.IsFinite(clipEnd) || durationMilliseconds <= 0 || clipEnd <= clipStartMilliseconds) return [];
        var sorted = points.OrderBy(p => p.Timestamp).ToArray();
        var timeline = new Services.ControlPointVolumeTimeline(sorted.Select(p =>
            new Core.Entities.ControlPointVolumeSnapshot(p.Timestamp, p.Volume, p.IsDisabled, p.IsRamp)).ToArray());
        var result = new List<OHsControlPointCurveProjection>();
        for (var i = 0; i < sorted.Length; i++)
        {
            var point = sorted[i];
            var start = Math.Max(Math.Max(0, clipStartMilliseconds), point.Timestamp);
            var end = Math.Min(clipEnd, i + 1 < sorted.Length ? sorted[i + 1].Timestamp : durationMilliseconds);
            if (end <= start) continue;
            var segment = timeline.Segments.LastOrDefault(p => p.StartTimestamp <= start);
            double Value(double time) => segment is null ? 100 : !segment.IsRamp || segment.EndTimestamp is not { } finish
                ? segment.StartVolume : segment.StartVolume + (segment.EndVolume - segment.StartVolume) *
                    Math.Clamp((time - segment.StartTimestamp) / (finish - (double)segment.StartTimestamp), 0, 1);
            double? connector = null;
            if (start == point.Timestamp && i > 0 && !point.IsDisabled)
            {
                var previous = timeline.Segments.LastOrDefault(p => p.StartTimestamp < point.Timestamp);
                var left = previous is null ? 100 : previous.IsRamp ? previous.EndVolume : previous.StartVolume;
                if (left != Value(start)) connector = left;
            }
            result.Add(new(point.Id, start, end, Value(start), Value(end), connector));
        }
        return result.AsReadOnly();
    }

    public static double? GetPlaybackMarkerX(double positionMilliseconds, double durationMilliseconds,
        double contentWidth, double horizontalOffset, double viewportWidth)
    {
        if (!double.IsFinite(positionMilliseconds) || !double.IsFinite(durationMilliseconds) ||
            !double.IsFinite(contentWidth) || !double.IsFinite(horizontalOffset) || !double.IsFinite(viewportWidth) ||
            durationMilliseconds <= 0 || contentWidth <= 0 || viewportWidth <= 0 || horizontalOffset < 0 ||
            positionMilliseconds < 0 || positionMilliseconds > durationMilliseconds) return null;
        var x = positionMilliseconds / durationMilliseconds * contentWidth - horizontalOffset;
        return double.IsFinite(x) && x >= 0 && x <= viewportWidth ? x : null;
    }

    public static int? GetDraggedVolume(int originalVolume, double startY, double pointerY, double usableHeight)
    {
        if (!double.IsFinite(startY) || !double.IsFinite(pointerY) || !double.IsFinite(usableHeight) || usableHeight <= 0) return null;
        return (int)Math.Clamp(Math.Round(originalVolume + (startY - pointerY) / usableHeight * 100, MidpointRounding.AwayFromZero),
            Core.Entities.ControlPoint.MinimumEditableVolume, Core.Entities.ControlPoint.MaximumVolume);
    }

    public static double GetLeft(int timestamp, double durationMilliseconds, double contentWidth) =>
        timestamp >= 0 && double.IsFinite(durationMilliseconds) && durationMilliseconds > 0 &&
        double.IsFinite(contentWidth) && contentWidth > 0
            ? timestamp / durationMilliseconds * contentWidth : double.NaN;

    public static IReadOnlyList<OHsControlPointExtent> GetExtents(IReadOnlyList<ControlPointSnapshot> points,
        double durationMilliseconds, double contentWidth)
    {
        if (!double.IsFinite(durationMilliseconds) || durationMilliseconds <= 0 || !double.IsFinite(contentWidth) || contentWidth <= 0) return [];
        var sorted = points.OrderBy(p => p.Timestamp).ToArray();
        var result = new OHsControlPointExtent[sorted.Length];
        for (var index = 0; index < sorted.Length; index++)
        {
            var start = Math.Clamp(sorted[index].Timestamp, 0, durationMilliseconds);
            var end = index + 1 < sorted.Length ? Math.Clamp(sorted[index + 1].Timestamp, start, durationMilliseconds) : durationMilliseconds;
            var left = start / durationMilliseconds * contentWidth;
            result[index] = new(sorted[index].Id, sorted[index].Timestamp, left, Math.Max(0, end / durationMilliseconds * contentWidth - left));
        }
        return result;
    }

    public static bool CanPlace(Guid excludedId, int timestamp, double durationMilliseconds, IReadOnlyList<ControlPointSnapshot> points) =>
        double.IsFinite(durationMilliseconds) && timestamp >= 0 && timestamp < durationMilliseconds &&
        !points.Any(p => p.Id == excludedId && p.IsOffsetAnchor) &&
        !points.Any(p => p.Id != excludedId && p.Timestamp == timestamp);
}
