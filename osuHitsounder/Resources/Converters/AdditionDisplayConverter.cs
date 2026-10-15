using System.Globalization;
using System.Windows.Data;

namespace osuHitsounder.Resources.Converters;

public sealed class AdditionDisplayConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not string text) return string.Empty;
        if (values.Length > 1 && values[1] is true) return text;
        return text switch { "Normal" => "N", "Drum" => "D", "Soft" => "S", "None" => "X", _ => text };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
