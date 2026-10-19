namespace osuHitsounder.Resources.Helpers;

public static class OHsTrackPalette
{
    public static string GetResourceKey(int zeroBasedIndex) => $"Brush.Track.{(zeroBasedIndex < 0 ? 0 : zeroBasedIndex % 8) + 1}";
}
