using System.Windows;
using System.Windows.Media;
using Hitbench.Resources.Helpers;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsTrackPaletteTests
{
    [Test]
    public void DefaultConsumersFollowThemeReplacementWithoutChangingData()
    {
        using var context = new WorkstationTrackDragTestContext();
        var row = context.Row(0);
        var piano = new Hitbench.Resources.CustomControls.PianoRollView();
        using var pianoView = new InMemoryView(new Window { Content = piano });
        foreach (var theme in new[] { "Dark", "Light", "Dark" })
        {
            Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
            { Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
            row.Resources.MergedDictionaries.Clear();
            row.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative) });
            piano.Resources.MergedDictionaries.Clear();
            piano.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative) });
            UiTestPump.Run(row.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
            context.View.UpdateLayout();
            pianoView.UpdateLayout();
            Assert.That(row.TrackBrush, Is.SameAs(row.FindResource("Brush.Track.Default")));
            Assert.That(piano.MarkerBrush, Is.SameAs(piano.FindResource("Brush.Track.Default")));
            Assert.That(context.Vm.Tracks[0].Color, Is.Null);
        }
        Assert.That(context.Saves.Writes, Is.Zero);
    }

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
