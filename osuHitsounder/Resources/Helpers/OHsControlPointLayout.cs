using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Resources.Helpers;

public static class OHsControlPointLayout
{
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
        !points.Any(p => p.Id != excludedId && p.Timestamp == timestamp);
}
