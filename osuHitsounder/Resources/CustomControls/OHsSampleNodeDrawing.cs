using System.Windows;
using System.Windows.Media;
namespace osuHitsounder.Resources.CustomControls;

public static class OHsSampleNodeDrawing
{
    public static Rect GetBounds(int timestamp, TimeSpan duration, double contentWidth, double horizontalOffset, double laneTop, double laneHeight, double zoomFactor)
    {
        if (duration <= TimeSpan.Zero || contentWidth <= 0 || laneHeight <= 0) return Rect.Empty;
        var zoom = double.IsFinite(zoomFactor) && zoomFactor > 0 ? Math.Clamp(zoomFactor, 8, 128) : 32;
        var width = 40 * Math.Min(zoom / 32, 1);
        return new(timestamp / duration.TotalMilliseconds * contentWidth - horizontalOffset - width / 2, laneTop + laneHeight / 4, width, laneHeight / 2);
    }
    public static Rect Inset(Rect r) => r.Width > 1 && r.Height > 1 ? new(r.X + .5, r.Y + .5, r.Width - 1, r.Height - 1) : r;
    public static void Draw(DrawingContext context, Brush? fill, Pen? border, Rect bounds) => context.DrawRoundedRectangle(fill, border, bounds, 4, 4);
}
