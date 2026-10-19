using System.Windows;
using System.Windows.Controls;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ValueSliderTests
{
    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        foreach (var resource in new[] { "Themes/Dark.xaml", "Themes/Tokens.xaml", "Styles/TextBlock.xaml",
                     "Styles/Border.xaml", "Styles/ContentPresenter.xaml", "Styles/Button.xaml",
                     "Styles/ScrollViewer.xaml", "Styles/Slider.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
            });
    }

    [Test]
    public void DisplaysCustomTextAndRetainsNativeRangeBehavior()
    {
        var slider = new ValueSlider
        {
            Minimum = 5,
            Maximum = 100,
            Value = 50,
            DisplayText = "custom text",
            Width = 180,
            Style = (Style)Application.Current.FindResource("BaseValueSliderStyle")
        };
        UiTestPump.Layout(slider, 180, 40);

        Assert.Multiple(() =>
        {
            Assert.That(slider.DisplayText, Is.EqualTo("custom text"));
            Assert.That(slider.Template.FindName("PART_Track", slider), Is.Not.Null);
        });

        slider.Value = 200;
        Assert.That(slider.Value, Is.EqualTo(100));
    }
}
