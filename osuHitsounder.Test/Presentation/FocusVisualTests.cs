using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class FocusVisualTests
{
    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        foreach (var resource in new[] { "Themes/Dark.xaml", "Converters/Converters.xaml", "Icons.xaml",
                     "Styles/TextBlock.xaml", "Styles/Border.xaml", "Styles/Button.xaml", "Styles/ScrollViewer.xaml",
                     "Styles/TextBox.xaml", "Styles/ComboBox.xaml", "Styles/Slider.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
            });
    }

    [Test]
    public void SharedFocusVisualHasNoUnderlineOrEnclosingBorder()
    {
        var visual = new Control { Style = (Style)Application.Current.FindResource("DefaultFocusVisualStyle") };
        UiTestPump.Layout(visual, 160, 40);
        Assert.Multiple(() =>
        {
            Assert.That(visual.Template, Is.Null);
            Assert.That(VisualTreeHelper.GetChildrenCount(visual), Is.Zero);
            Assert.That(visual.ActualHeight, Is.EqualTo(40));
        });
        UiTestPump.Layout(visual, 240, 40);
        Assert.That(VisualTreeHelper.GetChildrenCount(visual), Is.Zero);
        Assert.That(visual.ActualWidth, Is.EqualTo(240));
    }

    [TestCase("BaseTextBoxStyle")]
    [TestCase("BasePasswordBoxStyle")]
    [TestCase("BaseNumericUpDownStyle")]
    public void InputFamiliesReuseSharedFocusCue(string key)
    {
        var style = (Style)Application.Current.FindResource(key);
        Control control = key switch
        {
            "BaseTextBoxStyle" => new TextBox(),
            "BasePasswordBoxStyle" => new PasswordBox(),
            _ => new NumericUpDown()
        };
        control.Style = style;
        Assert.That(control.FocusVisualStyle, Is.SameAs(Application.Current.FindResource("DefaultFocusVisualStyle")));
    }

    [Test]
    public void NumericValidationAndDisabledStateRemainVisible()
    {
        var input = new NumericUpDown { Style = (Style)Application.Current.FindResource(typeof(NumericUpDown)) };
        var binding = new Binding("Tag") { Source = new Control { Tag = 50d }, Mode = BindingMode.OneWay };
        BindingOperations.SetBinding(input, NumericUpDown.ValueProperty, binding);
        UiTestPump.Layout(input, 160, 40);
        var editor = (TextBox)input.Template.FindName("PART_TextBox", input);
        Assert.That(editor.FocusVisualStyle, Is.SameAs(Application.Current.FindResource("DefaultFocusVisualStyle")));
        Assert.That(editor.Template, Is.Not.Null);
        var expression = input.GetBindingExpression(NumericUpDown.ValueProperty)!;
        Validation.MarkInvalid(expression, new ValidationError(new ExceptionValidationRule(), expression, "Invalid value", null));
        var border = (Border)input.Template.FindName("InputRoot", input);
        Assert.That(border.BorderBrush, Is.SameAs(Application.Current.FindResource("Brush.Danger")));
        Validation.ClearInvalid(expression);
        input.IsEnabled = false;
        Assert.That(border.Opacity, Is.EqualTo(0.4));
    }
}
