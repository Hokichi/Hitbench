using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class KeyboardNavigationPolicyTests
{
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
    public void SharedFocusResourceHasNoUnderlineAndTemplateChildrenHaveNoTabStops()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        foreach (var resource in new[] { "Themes/Dark.xaml", "Converters/Converters.xaml", "Icons.xaml",
                     "Styles/TextBlock.xaml", "Styles/ContentPresenter.xaml", "Styles/Border.xaml", "Styles/Button.xaml",
                     "Styles/TextBox.xaml", "Styles/ScrollViewer.xaml", "Styles/Popup.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
            });
        var focus = new Control { Style = (Style)app.FindResource("DefaultFocusVisualStyle") };
        Assert.That(focus.Template, Is.Null);
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
