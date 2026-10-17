using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Resources.Helpers;

public static class OHsControlPointLayout
{
    public static double GetLeft(int timestamp, double durationMilliseconds, double contentWidth) =>
        timestamp >= 0 && double.IsFinite(durationMilliseconds) && durationMilliseconds > 0 &&
        double.IsFinite(contentWidth) && contentWidth > 0
            ? timestamp / durationMilliseconds * contentWidth : double.NaN;

    public static bool CanPlace(Guid excludedId, int timestamp, double left, double width,
        IReadOnlyList<OHsControlPointExtent> extents)
    {
        if (timestamp < 0 || !double.IsFinite(left) || !double.IsFinite(width) || left < 0 || width < 0) return false;
        foreach (var other in extents)
        {
            if (other.Id == excludedId) continue;
            if (!double.IsFinite(other.Left) || !double.IsFinite(other.Width) || other.Left < 0 || other.Width < 0 ||
                other.Timestamp == timestamp || left < other.Left + other.Width && other.Left < left + width) return false;
        }
        return true;
    }
}
