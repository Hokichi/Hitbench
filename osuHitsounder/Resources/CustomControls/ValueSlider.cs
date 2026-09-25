using System.Windows;
using System.Windows.Controls;

namespace osuHitsounder.Resources.CustomControls;

public class ValueSlider : Slider
{
    public static readonly DependencyProperty DisplayTextProperty = DependencyProperty.Register(
        nameof(DisplayText), typeof(string), typeof(ValueSlider), new PropertyMetadata(string.Empty));

    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }
}
