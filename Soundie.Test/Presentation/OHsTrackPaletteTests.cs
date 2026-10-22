using System.Windows;
using System.Windows.Media;
using Soundie.Resources.Helpers;
namespace Soundie.Test.Presentation;
[Apartment(ApartmentState.STA)]
public class OHsTrackPaletteTests
{
    [Test] public void CustomDomainColorOverridesPaletteAndFallbackResolvesResource()
    {
        var owner=new FrameworkElement();var automatic=new SolidColorBrush(Colors.Green);owner.Resources["Brush.Track.1"]=automatic;
        Assert.That(OHsTrackPalette.ResolveBrush(owner,0,null),Is.SameAs(automatic));
        var custom=(SolidColorBrush)OHsTrackPalette.ResolveBrush(owner,0,"#123456");
        Assert.That(custom.Color,Is.EqualTo(Color.FromRgb(0x12,0x34,0x56)));Assert.That(custom.IsFrozen,Is.True);
    }
}
