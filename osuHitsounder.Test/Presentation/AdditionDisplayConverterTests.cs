using System.Globalization;
using osuHitsounder.Resources.Converters;
namespace osuHitsounder.Test.Presentation;
public class AdditionDisplayConverterTests
{
    [TestCase("Normal", "N")]
    [TestCase("Drum", "D")]
    [TestCase("Soft", "S")]
    [TestCase("None", "X")]
    public void CompactSelectionPreservesFullOptions(string value, string compact)
    {
        var converter = new AdditionDisplayConverter();
        Assert.That(converter.Convert([value, false], typeof(string), null!, CultureInfo.InvariantCulture), Is.EqualTo(compact));
        Assert.That(converter.Convert([value, true], typeof(string), null!, CultureInfo.InvariantCulture), Is.EqualTo(value));
        Assert.That(converter.ConvertBack(compact, [typeof(string), typeof(bool)], null!, CultureInfo.InvariantCulture), Is.All.SameAs(System.Windows.Data.Binding.DoNothing));
    }
}
