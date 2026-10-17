using System.Windows;

namespace osuHitsounder.Resources.Helpers;

public static class OHsTrackDragGeometry
{
    public static bool CrossedThreshold(Point start, Point current, double horizontal, double vertical) =>
        IsFinite(start) && IsFinite(current) && double.IsFinite(horizontal) && horizontal > 0 &&
        double.IsFinite(vertical) && vertical > 0 &&
        (Math.Abs(current.X - start.X) >= horizontal || Math.Abs(current.Y - start.Y) >= vertical);

    public static int? ToFinalIndex(int sourceIndex, int insertionSlot, int trackCount) =>
        sourceIndex < 0 || sourceIndex >= trackCount || insertionSlot < 0 || insertionSlot > trackCount
            ? null : insertionSlot > sourceIndex ? insertionSlot - 1 : insertionSlot;

    public static int? FindInsertionSlot(Point pointer, Rect viewport,
        IReadOnlyList<(int Index, Rect Bounds)> rows, int trackCount,
        int? currentSlot = null, Rect? gapBounds = null)
    {
        if (!IsFinite(pointer) || !IsValid(viewport) || !viewport.Contains(pointer) || rows.Count == 0 || trackCount <= 0)
            return null;
        var previous = -1;
        foreach (var (index, bounds) in rows)
        {
            if (index <= previous || index >= trackCount || !IsValid(bounds)) return null;
            previous = index;
        }
        var gap = gapBounds is { } value && IsValid(value) ? value : Rect.Empty;
        if (!gap.IsEmpty && gap.Contains(pointer) && currentSlot >= 0 && currentSlot <= trackCount) return currentSlot;
        var y = pointer.Y - (!gap.IsEmpty && pointer.Y >= gap.Bottom ? gap.Height : 0);
        foreach (var (index, bounds) in rows)
        {
            var top = bounds.Top - (!gap.IsEmpty && bounds.Top >= gap.Bottom ? gap.Height : 0);
            if (y < top + bounds.Height / 2) return index;
        }
        return rows[^1].Index + 1;
    }

    private static bool IsFinite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
    private static bool IsValid(Rect bounds) => !bounds.IsEmpty && IsFinite(bounds.TopLeft) &&
        double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) && bounds.Width > 0 && bounds.Height > 0;
}
