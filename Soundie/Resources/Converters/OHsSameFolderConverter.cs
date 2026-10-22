using System.Globalization;
using System.Windows.Data;
using Soundie.Resources.DataModels;

namespace Soundie.Resources.Converters;

public sealed class OHsSameFolderConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is FolderEntry left && values[1] is FolderEntry right &&
        string.Equals(left.FullName, right.FullName, StringComparison.OrdinalIgnoreCase);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
