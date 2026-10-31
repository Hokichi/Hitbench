using System.Windows;
using System.Windows.Media;
using Hitbench.Resources.Helpers;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA)]
public class OHsTrackPaletteTests
{
    [TestCase(-1)] [TestCase(0)] [TestCase(8)] [TestCase(999)]
    public void DefaultKeyDoesNotDependOnTrackIndex(int index) =>
        Assert.That(OHsTrackPalette.GetResourceKey(index), Is.EqualTo("Brush.Track.Default"));

    [TestCase("Dark", 255)] [TestCase("Light", 0)]
    public void DefaultResolutionFollowsTheme(string theme, byte channel)
    {
        var owner = new FrameworkElement();
        owner.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative) });
        var brush = (SolidColorBrush)OHsTrackPalette.ResolveBrush(owner, 7, null);
        Assert.That(brush, Is.SameAs(owner.FindResource("Brush.Track.Default")));
        Assert.That(brush.Color, Is.EqualTo(Color.FromRgb(channel, channel, channel)));
    }

    [Test] public void CustomDomainColorOverridesPaletteAndFallbackResolvesResource()
    {
        var owner=new FrameworkElement();var automatic=new SolidColorBrush(Colors.Green);owner.Resources["Brush.Track.Default"]=automatic;
        Assert.That(OHsTrackPalette.ResolveBrush(owner,0,null),Is.SameAs(automatic));
        var custom=(SolidColorBrush)OHsTrackPalette.ResolveBrush(owner,0,"#123456");
        Assert.That(custom.Color,Is.EqualTo(Color.FromRgb(0x12,0x34,0x56)));Assert.That(custom.IsFrozen,Is.True);
    }
}
