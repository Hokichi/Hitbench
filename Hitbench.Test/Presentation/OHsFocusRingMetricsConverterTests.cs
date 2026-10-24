using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Hitbench.Resources.Converters;

namespace Hitbench.Test.Presentation;

public sealed class OHsFocusRingMetricsConverterTests
{
    [TestCase("Stroke", 3d)]
    [TestCase("Outset", -8d)]
    public void ComputesFromExplicitInputs(string mode, double expected) =>
        Assert.That(new OHsFocusRingMetricsConverter().Convert([3d, 5d], typeof(Thickness), mode,
            CultureInfo.InvariantCulture), Is.EqualTo(new Thickness(expected)));

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(-1d)]
    public void RejectsInvalidMetric(double metric) =>
        Assert.That(new OHsFocusRingMetricsConverter().Convert([metric, 5d], typeof(Thickness), "Stroke",
            CultureInfo.InvariantCulture), Is.SameAs(DependencyProperty.UnsetValue));

    [Test]
    public void ReversePreservesEachSource() =>
        Assert.That(new OHsFocusRingMetricsConverter().ConvertBack(new Thickness(3), [typeof(double), typeof(double)],
            "Stroke", CultureInfo.InvariantCulture), Is.EqualTo(new[] { Binding.DoNothing, Binding.DoNothing }));
}
