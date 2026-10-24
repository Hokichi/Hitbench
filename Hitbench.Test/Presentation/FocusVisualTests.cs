using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Hitbench.Resources.CustomControls;

namespace Hitbench.Test.Presentation;

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
        foreach (var resource in new[] { "Themes/Dark.xaml", "Themes/Tokens.xaml", "Converters/Converters.xaml", "Icons.xaml",
                     "Styles/TextBlock.xaml", "Styles/Border.xaml", "Styles/Button.xaml", "Styles/ScrollViewer.xaml",
                     "Styles/TextBox.xaml", "Styles/ComboBox.xaml", "Styles/Slider.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/Hitbench;component/Resources/{resource}", UriKind.Relative)
            });
    }

    [Test]
    public void SharedFocusVisualUsesAssignedResourceAcrossResize()
    {
        var visual = new Control { Style = (Style)Application.Current.FindResource("DefaultFocusVisualStyle") };
        foreach (var width in new[] { 160d, 240d })
        {
            UiTestPump.Layout(visual, width, 40);
            UiControlContract.AssertStyle<Control>(visual, "DefaultFocusVisualStyle");
        }
    }

    [Test]
    public void KeyboardFocusVisualInheritsDefaultFocusContract()
    {
        var visual = new Control { Style = (Style)Application.Current.FindResource("DefaultFocusVisualStyle") };
        UiControlContract.AssertStyle<Control>(visual, "DefaultFocusVisualStyle");
        Assert.That(visual.Style, Is.SameAs(Application.Current.FindResource("DefaultFocusVisualStyle")));
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
    public void NumericValidationAndDisabledStateRemainFunctional()
    {
        var input = new NumericUpDown { Style = (Style)Application.Current.FindResource(typeof(NumericUpDown)) };
        var binding = new Binding("Tag") { Source = new Control { Tag = 50d }, Mode = BindingMode.OneWay };
        BindingOperations.SetBinding(input, NumericUpDown.ValueProperty, binding);
        UiTestPump.Layout(input, 160, 40);
        var editor = (TextBox)input.Template.FindName("PART_TextBox", input);
        Assert.That(editor.FocusVisualStyle, Is.SameAs(Application.Current.FindResource("OHsSampleManagerKeyboardFocusStyle")));
        UiControlContract.AssertStyle<NumericUpDown>(input, typeof(NumericUpDown));
        UiControlContract.AssertTemplate(input, "NumericUpDownTemplate");
        UiControlContract.AssertStyle<TextBox>(editor, "NumericUpDownEditorStyle");
        var expression = input.GetBindingExpression(NumericUpDown.ValueProperty)!;
        Validation.MarkInvalid(expression, new ValidationError(new ExceptionValidationRule(), expression, "Invalid value", null));
        Assert.That(Validation.GetHasError(input), Is.True);
        Validation.ClearInvalid(expression);
        input.IsEnabled = false;
        Assert.That(Validation.GetHasError(input), Is.False);
        Assert.That(editor.IsEnabled, Is.False);
    }
}
