using System.Windows;
using System.Windows.Controls;
using Onset.Resources.CustomControls;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class SharedUiResourceIntegrationTests
{
    [TestCase("Dark")]
    [TestCase("Light")]
    public void SegmentedResourcesPreserveContractsAndKeyboardSelection(string theme)
    {
        SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var app = Application.Current;
        app.Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri($"/Onset;component/Resources/Themes/{theme}.xaml", UriKind.Relative)
        };
        foreach (var family in new[] { "SegmentedToggleGroup", "SegmentedToggleOption" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/Onset;component/Resources/Styles/{family}.xaml", UriKind.Relative)
            });
        var group = new SegmentedToggleGroup();
        var first = new SegmentedToggleOption { Content = "First", Value = "first" };
        var second = new SegmentedToggleOption { Content = "Second", Value = "second" };
        group.Items.Add(first);
        group.Items.Add(second);
        var host = new Grid { Resources = app.Resources };
        host.Children.Add(group);
        UiTestPump.Layout(host, 400, 100);
        UiControlContract.AssertStyle<SegmentedToggleGroup>(group, typeof(SegmentedToggleGroup));
        UiControlContract.AssertTemplate(group, "SegmentedToggleGroupTemplate");
        UiControlContract.AssertStyle<SegmentedToggleOption>(first, typeof(SegmentedToggleOption));
        UiControlContract.AssertTemplate(first, "SegmentedToggleOptionTemplate");
        Assert.That(first.IsTabStop, Is.True);
        Assert.That(first.FocusVisualStyle, Is.SameAs(first.FindResource("OHsSampleManagerKeyboardFocusStyle")));
        var changes = 0;
        group.OptionSelected += (_, _) => changes++;
        var provider = (System.Windows.Automation.Provider.ISelectionItemProvider)
            System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(first)
                .GetPattern(System.Windows.Automation.Peers.PatternInterface.SelectionItem);
        provider.Select();
        UiTestPump.Run(first.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
        Assert.That(group.SelectedValue, Is.EqualTo("first"));
        Assert.That(changes, Is.EqualTo(1));
        second.IsEnabled = false;
        var disabled = (System.Windows.Automation.Provider.ISelectionItemProvider)
            System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(second)
                .GetPattern(System.Windows.Automation.Peers.PatternInterface.SelectionItem);
        Assert.Throws<System.Windows.Automation.ElementNotEnabledException>(() => disabled.Select());
        Assert.That(group.SelectedValue, Is.EqualTo("first"));
    }
    [TestCase(typeof(BalloonButton), "BalloonControlTemplate")]
    [TestCase(typeof(BalloonCheckBox), "BalloonControlTemplate")]
    [TestCase(typeof(NumericUpDown), "NumericUpDownTemplate")]
    [TestCase(typeof(BasePopup), "BasePopupTemplate")]
    public void ImplicitControlsResolveCanonicalTemplates(Type type, string templateKey)
    {
        SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var control = (Control)Activator.CreateInstance(type)!;
        try
        {
            Assert.That(control.GetType(), Is.EqualTo(type));
            if (control is not Window)
            {
                var host = new Grid { Resources = Application.Current.Resources };
                host.Children.Add(control);
                UiTestPump.Layout(host, 300, 100);
            }
            UiControlContract.AssertTemplate(control, templateKey);
        }
        finally { if (control is Window window) window.Close(); }
    }

    [Test]
    public void SharedResources_DeferApplicationFontLookupUntilFontsAreAvailable()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.Clear();

        Assert.DoesNotThrow(() =>
        {
            foreach (var resource in new[]
                     {
                         "Themes/Dark.xaml", "Themes/Tokens.xaml", "Converters/Converters.xaml", "Icons.xaml",
                         "Styles/ColumnDefinition.xaml", "Styles/Grid.xaml", "Styles/StackPanel.xaml",
                         "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/Image.xaml", "Styles/Border.xaml",
                         "Styles/ContentPresenter.xaml", "Styles/Button.xaml", "Styles/MenuItem.xaml", "Styles/ContextMenu.xaml", "Styles/BalloonComboBox.xaml", "Styles/SegmentedToggleGroup.xaml", "Styles/SegmentedToggleOption.xaml", "Styles/PianoRollView.xaml", "Styles/BeatGridView.xaml", "Styles/ToolTip.xaml",
                         "Styles/Window.xaml", "Styles/ListBox.xaml", "Styles/ListView.xaml", "Styles/TreeView.xaml",
                         "Styles/ComboBox.xaml", "Styles/ScrollViewer.xaml", "Styles/Slider.xaml", "Styles/TextBox.xaml", "Styles/ProgressBar.xaml",
                         "Styles/StepNavigator.xaml", "Styles/Popup.xaml"
                     })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/Onset;component/Resources/{resource}", UriKind.Relative)
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
                     "Themes/Dark.xaml", "Themes/Tokens.xaml", "Converters/Converters.xaml", "Icons.xaml",
                     "Styles/ColumnDefinition.xaml", "Styles/Grid.xaml", "Styles/StackPanel.xaml",
                     "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/Image.xaml", "Styles/Border.xaml",
                     "Styles/ContentPresenter.xaml", "Styles/Button.xaml", "Styles/MenuItem.xaml", "Styles/ContextMenu.xaml", "Styles/BalloonComboBox.xaml", "Styles/SegmentedToggleGroup.xaml", "Styles/SegmentedToggleOption.xaml", "Styles/PianoRollView.xaml", "Styles/BeatGridView.xaml", "Styles/ToolTip.xaml",
                     "Styles/Window.xaml", "Styles/ListBox.xaml", "Styles/ListView.xaml", "Styles/TreeView.xaml",
                     "Styles/ComboBox.xaml", "Styles/ScrollViewer.xaml", "Styles/Slider.xaml", "Styles/TextBox.xaml", "Styles/ProgressBar.xaml",
                     "Styles/StepNavigator.xaml", "Styles/Popup.xaml"
                 })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/Onset;component/Resources/{resource}", UriKind.Relative)
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
            Source = new Uri("/Onset;component/Resources/Themes/Dark.xaml", UriKind.Relative)
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Onset;component/Resources/Converters/Converters.xaml", UriKind.Relative)
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Onset;component/Resources/Themes/Tokens.xaml", UriKind.Relative)
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Onset;component/Resources/Styles/ContentPresenter.xaml", UriKind.Relative)
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Onset;component/Resources/Styles/Border.xaml", UriKind.Relative)
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Onset;component/Resources/Styles/Button.xaml", UriKind.Relative)
        });

        var style = app.TryFindResource(styleKey) as Style;
        Assert.That(style, Is.Not.Null);
        Assert.That(style!.TargetType, Is.EqualTo(typeof(BalloonButton)));

        var button = new BalloonButton { Style = style };
        UiControlContract.AssertStyle<BalloonButton>(button, styleKey);
        UiControlContract.AssertTemplate(button, "BalloonControlTemplate");
    }
}

