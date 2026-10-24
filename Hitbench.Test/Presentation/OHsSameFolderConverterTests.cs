using System.Globalization;
using System.Windows.Data;
using Hitbench.Resources.Converters;
using Hitbench.Resources.DataModels;

namespace Hitbench.Test.Presentation;

public sealed class OHsSameFolderConverterTests
{
    [Test]
    public void ComparesPathsAndNeverWritesBack()
    {
        var converter = new OHsSameFolderConverter();
        var entry = new FolderEntry("C:\\Mapset", "map", default);
        Assert.That(converter.Convert([entry, entry with { FullName = "c:\\mapset" }], typeof(bool), null!, CultureInfo.InvariantCulture), Is.True);
        Assert.That(converter.Convert([entry, null!], typeof(bool), null!, CultureInfo.InvariantCulture), Is.False);
        Assert.That(converter.ConvertBack(true, [typeof(object), typeof(object)], null!, CultureInfo.InvariantCulture),
            Is.EqualTo(new[] { Binding.DoNothing, Binding.DoNothing }));
    }
}
