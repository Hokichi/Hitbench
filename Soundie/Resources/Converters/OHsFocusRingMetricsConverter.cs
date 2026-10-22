using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Soundie.Resources.Converters;

public sealed class OHsFocusRingMetricsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 2 || values[0] is not double thickness || values[1] is not double gap ||
            !double.IsFinite(thickness) || !double.IsFinite(gap) || thickness < 0 || gap < 0)
            return DependencyProperty.UnsetValue;
        return parameter switch
        {
            "Stroke" => new Thickness(thickness),
            "Outset" when double.IsFinite(thickness + gap) => new Thickness(-thickness - gap),
            _ => DependencyProperty.UnsetValue
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        Array.ConvertAll(targetTypes, _ => Binding.DoNothing);
}
