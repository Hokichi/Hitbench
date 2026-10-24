using System.Globalization;
namespace Onset.Core.Entities;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static bool TryParse(string? text, out RgbColor color)
    {
        color = default;
        if (text is not { Length: 7 } || text[0] != '#' ||
            !uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb)) return false;
        color = new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
    public static RgbColor FromHsv(double hue, double saturation, double value)
    {
        if (!double.IsFinite(hue) || hue is < 0 or > 360) throw new ArgumentOutOfRangeException(nameof(hue));
        if (!double.IsFinite(saturation) || saturation is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(saturation));
        if (!double.IsFinite(value) || value is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(value));
        var h = hue % 360 / 60;
        var c = value * saturation; var x = c * (1 - Math.Abs(h % 2 - 1)); var m = value - c;
        var (r,g,b) = h switch { <1 => (c,x,0d), <2 => (x,c,0d), <3 => (0d,c,x), <4 => (0d,x,c), <5 => (x,0d,c), _ => (c,0d,x) };
        return new((byte)Math.Round((r+m)*255), (byte)Math.Round((g+m)*255), (byte)Math.Round((b+m)*255));
    }
    public (double Hue, double Saturation, double Value) ToHsv()
    {
        var r=R/255d; var g=G/255d; var b=B/255d; var max=Math.Max(r,Math.Max(g,b)); var min=Math.Min(r,Math.Min(g,b)); var delta=max-min;
        var h=delta == 0 ? 0 : max == r ? 60*((g-b)/delta % 6) : max == g ? 60*((b-r)/delta+2) : 60*((r-g)/delta+4);
        return (h<0?h+360:h, max == 0 ? 0 : delta/max, max);
    }
}
