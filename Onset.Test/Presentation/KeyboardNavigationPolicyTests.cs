using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Onset.Resources.CustomControls;
using Onset.Resources.Helpers;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class KeyboardNavigationPolicyTests
{
    [Test]
    public void ExplicitOptInHonorsRequestedTabStopAndCanBeRemoved()
    {
        var input = new TextBox();
        KeyboardNavigation.SetIsTabStop(input, true);
        Assert.That(input.IsTabStop, Is.False);
        KeyboardNavigationPolicy.SetAllowTabNavigation(input, true);
        Assert.That(input.IsTabStop, Is.True);
        KeyboardNavigation.SetIsTabStop(input, false);
        Assert.That(input.IsTabStop, Is.False);
        KeyboardNavigation.SetIsTabStop(input, true);
        KeyboardNavigationPolicy.SetAllowTabNavigation(input, false);
        Assert.That(input.IsTabStop, Is.False);
        Assert.That(new BalloonButton().IsTabStop, Is.False);
    }

    [Test]
    public void ManagerFocusStyleUsesNamedTemplateAndPreservesDefaultFocusPolicy()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var focus = new Control();
        focus.Style = (Style)Application.Current.FindResource("OHsSampleManagerKeyboardFocusStyle");
        Assert.That(focus.Style.BasedOn, Is.SameAs(focus.FindResource("DefaultFocusVisualStyle")));
        Assert.That(focus.Template, Is.SameAs(focus.FindResource("OHsKeyboardFocusRingTemplate")));
        var ordinary = new Control { Style = (Style)focus.FindResource("DefaultFocusVisualStyle") };
        UiControlContract.AssertTemplate(ordinary, "OHsKeyboardFocusRingTemplate");
    }

    [Test]
    public void NativeAndCustomControlsCannotBecomeTabStops()
    {
        UIElement[] controls = [new Button(), new TextBox(), new PasswordBox(), new ComboBox(),
            new ComboBoxItem(), new Slider(), new Thumb(), new RepeatButton(), new ScrollViewer(),
            new ListBoxItem(), new ListViewItem(), new BalloonButton(), new BalloonCheckBox(),
            new BalloonRadioButton(), new NumericUpDown(), new BeatGridView(), new VolumeWheel()];
        foreach (var element in controls)
        {
            KeyboardNavigation.SetIsTabStop(element, true);
            Assert.That(KeyboardNavigation.GetIsTabStop(element), Is.False, element.GetType().Name);
            if (element is Control control)
            {
                control.Style = new Style(control.GetType())
                {
                    Setters = { new Setter(Control.IsTabStopProperty, true) }
                };
                control.ClearValue(Control.IsTabStopProperty);
                Assert.That(control.IsTabStop, Is.False, $"Style on {element.GetType().Name}");
            }
        }
    }

    [Test]
    public void TextEditingAndFocusableContractRemainAvailable()
    {
        var input = new TextBox { Text = "Kick" };
        Assert.That(input.Focusable, Is.True);
        input.SelectAll();
        input.SelectedText = "Snare";
        Assert.That(input.Text, Is.EqualTo("Snare"));
        Assert.That(input.IsTabStop, Is.False);
    }

    [Test]
    public void SharedFocusResourcePreservesNonInteractiveTemplateChildren()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        foreach (var resource in new[] { "Themes/Dark.xaml", "Themes/Tokens.xaml", "Converters/Converters.xaml", "Icons.xaml",
                     "Styles/TextBlock.xaml", "Styles/ContentPresenter.xaml", "Styles/Border.xaml", "Styles/Button.xaml",
                     "Styles/TextBox.xaml", "Styles/ScrollViewer.xaml", "Styles/Popup.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/Onset;component/Resources/{resource}", UriKind.Relative)
            });
        var focus = new Control { Style = (Style)app.FindResource("DefaultFocusVisualStyle") };
        UiControlContract.AssertTemplate(focus, "OHsKeyboardFocusRingTemplate");
        var numeric = new NumericUpDown { Style = (Style)app.FindResource(typeof(NumericUpDown)) };
        UiTestPump.Layout(numeric, 220, 40);
        Assert.That(numeric.ApplyTemplate() || VisualTreeHelper.GetChildrenCount(numeric) > 0, Is.True);
        var popup = new OHsMessageBoxPopup("Message", "Title");
        popup.ApplyTemplate();
        AssertNoTabStops(numeric);
        AssertNoTabStops(popup);
        popup.Close();
    }

    private static void AssertNoTabStops(DependencyObject root)
    {
        if (root is UIElement element)
            Assert.That(KeyboardNavigation.GetIsTabStop(element), Is.False, root.GetType().Name);
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            AssertNoTabStops(VisualTreeHelper.GetChild(root, index));
    }
}
