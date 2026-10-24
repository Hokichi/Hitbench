using System.Windows;
using System.Windows.Media;
using Hitbench.Core.Entities;
namespace Hitbench.Resources.Helpers;

public static class OHsTrackPalette
{
    public static string GetResourceKey(int zeroBasedIndex) => $"Brush.Track.{(zeroBasedIndex < 0 ? 0 : zeroBasedIndex % 8) + 1}";
    public static Brush ResolveBrush(FrameworkElement owner, int zeroBasedIndex, string? customColor)
    {
        if (!RgbColor.TryParse(customColor, out var rgb)) return owner.TryFindResource(GetResourceKey(zeroBasedIndex)) as Brush ?? SystemColors.WindowTextBrush;
        var brush=new SolidColorBrush(Color.FromRgb(rgb.R,rgb.G,rgb.B)); brush.Freeze(); return brush;
    }
}
