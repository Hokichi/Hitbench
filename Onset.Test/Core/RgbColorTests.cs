using Onset.Core.Entities;
namespace Onset.Test.Core;
public class RgbColorTests
{
    [TestCase("#aB12eF", true)] [TestCase("#FFF", false)] [TestCase("#00112233", false)]
    [TestCase("112233", false)] [TestCase(" #112233", false)] [TestCase("#GG1122", false)] [TestCase(null, false)]
    public void ParsesOnlyOpaqueHex(string? input, bool valid) => Assert.That(RgbColor.TryParse(input, out _), Is.EqualTo(valid));
    [TestCase(0, 1, 1, "#FF0000")] [TestCase(120, 1, 1, "#00FF00")] [TestCase(240, 1, 1, "#0000FF")]
    [TestCase(360, 1, 1, "#FF0000")] [TestCase(80, 0, 1, "#FFFFFF")] [TestCase(80, 1, 0, "#000000")]
    public void ConvertsHsv(double h, double s, double v, string hex) => Assert.That(RgbColor.FromHsv(h,s,v).ToHex(), Is.EqualTo(hex));
    [Test] public void NormalizesAndRoundTrips()
    {
        Assert.That(RgbColor.TryParse("#aB12eF", out var rgb), Is.True);
        Assert.That(rgb.ToHex(), Is.EqualTo("#AB12EF"));
        var (h,s,v)=rgb.ToHsv(); Assert.That(RgbColor.FromHsv(h,s,v), Is.EqualTo(rgb));
        Assert.Throws<ArgumentOutOfRangeException>(()=>RgbColor.FromHsv(double.NaN,0,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>RgbColor.FromHsv(0,-1,0));
    }
}
