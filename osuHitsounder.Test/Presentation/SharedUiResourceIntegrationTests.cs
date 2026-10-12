using System.Windows;
using System.Windows.Controls;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class SharedUiResourceIntegrationTests
{
    [Test]
    public void SharedResources_DeferApplicationFontLookupUntilFontsAreAvailable()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.Clear();

        Assert.DoesNotThrow(() =>
        {
            foreach (var resource in new[]
                     {
                         "Themes/Dark.xaml", "Converters/Converters.xaml", "Icons.xaml",
                         "Styles/ColumnDefinition.xaml", "Styles/Grid.xaml", "Styles/StackPanel.xaml",
                         "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/Image.xaml", "Styles/Border.xaml",
                         "Styles/ContentPresenter.xaml", "Styles/Button.xaml", "Styles/PianoRollView.xaml", "Styles/ToolTip.xaml",
                         "Styles/Window.xaml", "Styles/ListBox.xaml", "Styles/ListView.xaml", "Styles/TreeView.xaml",
                         "Styles/ComboBox.xaml", "Styles/ScrollViewer.xaml", "Styles/Slider.xaml", "BeatGrid.xaml", "Styles/TextBox.xaml", "Styles/ProgressBar.xaml",
                         "Styles/StepNavigator.xaml", "Styles/Popup.xaml"
                     })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
                });
        });

        UiTestPump.EnsureFontResources(app);
        var button = new BalloonButton
        {
            ButtonText = "Deferred font",
            Style = (Style)app.FindResource(typeof(BalloonButton))
        };

        Assert.That(button.ApplyTemplate(), Is.True);
        var text = (TextBlock?)button.Template.FindName("PART_ButtonText", button);
        Assert.That(text, Is.Not.Null);
        Assert.That(text!.Text, Is.EqualTo("Deferred font"));
        UiControlContract.AssertStyle<BalloonButton>(button, typeof(BalloonButton));
        UiControlContract.AssertTemplate(button, "BalloonControlTemplate");
    }

    [Test]
    public void SharedResources_LoadTogetherAndProvideReusableControlContracts()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);

        foreach (var resource in new[]
                 {
                     "Themes/Dark.xaml", "Converters/Converters.xaml", "Icons.xaml",
                     "Styles/ColumnDefinition.xaml", "Styles/Grid.xaml", "Styles/StackPanel.xaml",
                     "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/Image.xaml", "Styles/Border.xaml",
                     "Styles/ContentPresenter.xaml", "Styles/Button.xaml", "Styles/PianoRollView.xaml", "Styles/ToolTip.xaml",
                     "Styles/Window.xaml", "Styles/ListBox.xaml", "Styles/ListView.xaml", "Styles/TreeView.xaml",
                     "Styles/ComboBox.xaml", "Styles/ScrollViewer.xaml", "Styles/Slider.xaml", "BeatGrid.xaml", "Styles/TextBox.xaml", "Styles/ProgressBar.xaml",
                     "Styles/StepNavigator.xaml", "Styles/Popup.xaml"
                 })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
            });

        Assert.Multiple(() =>
        {
            Assert.That(app.TryFindResource("BackgroundToForegroundConverter"), Is.Not.Null);
            Assert.That(app.TryFindResource("OHsComboBoxStyle"), Is.TypeOf<Style>());
            Assert.That(app.TryFindResource(typeof(BalloonButton)), Is.TypeOf<Style>());
            Assert.That(app.TryFindResource(typeof(BasePopup)), Is.TypeOf<Style>());
            Assert.That(app.TryFindResource("Info"), Is.Not.Null);
        });

        var popup = new OHsMessageBoxPopup("Message", "Title");
        Assert.That(popup.ApplyTemplate(), Is.True);
        popup.Close();
    }

    [TestCase("OHsBalloonNeutralActionStyle")]
    [TestCase("OHsBalloonDefaultActionStyle")]
    [TestCase("OHsBalloonConfirmActionStyle")]
    [TestCase("OHsBalloonDestructiveActionStyle")]
    public void BalloonActionStyles_ProvideAssignedControlContracts(string styleKey)
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/osuHitsounder;component/Resources/Themes/Dark.xaml", UriKind.Relative)
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/osuHitsounder;component/Resources/Converters/Converters.xaml", UriKind.Relative)
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/osuHitsounder;component/Resources/Styles/ContentPresenter.xaml", UriKind.Relative)
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/osuHitsounder;component/Resources/Styles/Border.xaml", UriKind.Relative)
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/osuHitsounder;component/Resources/Styles/Button.xaml", UriKind.Relative)
        });

        var style = app.TryFindResource(styleKey) as Style;
        Assert.That(style, Is.Not.Null);
        Assert.That(style!.TargetType, Is.EqualTo(typeof(BalloonButton)));

        var button = new BalloonButton { Style = style };
        UiControlContract.AssertStyle<BalloonButton>(button, styleKey);
        UiControlContract.AssertTemplate(button, "BalloonControlTemplate");
    }
}
