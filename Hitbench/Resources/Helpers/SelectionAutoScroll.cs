namespace Hitbench.Resources.Helpers;

public static class SelectionAutoScroll
{
    public static double GetDelta(double pointer, double minimum, double maximum, TimeSpan elapsed)
    {
        if (!double.IsFinite(pointer) || !double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum || elapsed <= TimeSpan.Zero) return 0;
        var distance = pointer < minimum ? pointer - minimum : pointer > maximum ? pointer - maximum : 0;
        return distance == 0 ? 0 : Math.Sign(distance) * Math.Min(1000, 80 + 8 * Math.Abs(distance)) * Math.Min(.05, elapsed.TotalSeconds);
    }
}
