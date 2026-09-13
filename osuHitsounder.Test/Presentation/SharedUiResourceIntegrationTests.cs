using System.Windows;
using System.Windows.Controls;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class SharedUiResourceIntegrationTests
{
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
                     "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/Border.xaml",
                     "Styles/ContentPresenter.xaml", "Styles/Button.xaml", "Styles/ToolTip.xaml",
                     "Styles/Window.xaml", "Styles/ListBox.xaml", "Styles/ListView.xaml",
                     "Styles/ComboBox.xaml", "Styles/TextBox.xaml", "Styles/ProgressBar.xaml",
                     "Styles/ScrollViewer.xaml", "Styles/StepNavigator.xaml", "Styles/Popup.xaml"
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
}
