using System.Windows;
using System.Windows.Controls;
using Soundie.Resources.CustomControls;

namespace Soundie.Test.Presentation;

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
                Source = new Uri($"/Soundie;component/Resources/{resource}", UriKind.Relative)
            });
    }

    [Test]
    public void TagAdornerAttachesAndDetachesWithoutNativePopup()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var row = new Soundie.Views.Pages.OHsWorkstationTrackRow();
        var slider = new ValueSlider { ShowValueTag = true, DisplayText = "200%", ValueTagTemplate = (DataTemplate)row.FindResource("OHsWorkstationTrackVolumeTagTemplate"), Style = (Style)Application.Current.FindResource("BaseValueSliderStyle") };
        var decorator = new System.Windows.Documents.AdornerDecorator { Child = slider };
        using var view = new InMemoryView(new Window { Content = decorator, Width = 300, Height = 100 });
        slider.UpdateValueTagState(false, true, false);
        var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(slider);
        Assert.That(layer.GetAdorners(slider), Has.Length.EqualTo(1));
        view.UpdateLayout();
        slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert.That(layer.GetAdorners(slider), Is.Null);
    }

    [Test]
    public void TagVisibilityFollowsDragFocusAndDisabledState()
    {
        var slider = new ValueSlider { ShowValueTag = true };
        slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        slider.UpdateValueTagState(false, true, false);
        Assert.That(slider.IsValueTagVisible, Is.True);
        slider.UpdateValueTagState(false, false, true);
        Assert.That(slider.IsValueTagVisible, Is.True);
        slider.UpdateValueTagState(false, false, false);
        Assert.That(slider.IsValueTagVisible, Is.False);
        slider.UpdateValueTagState(true, false, false);
        Assert.That(slider.IsValueTagVisible, Is.True);
        slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert.That(slider.IsValueTagVisible, Is.False);
        slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        slider.IsEnabled = false;
        Assert.That(slider.IsValueTagVisible, Is.False);
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
