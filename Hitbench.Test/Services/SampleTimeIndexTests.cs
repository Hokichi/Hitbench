using Hitbench.Core.Entities;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public sealed class SampleTimeIndexTests
{
    [Test]
    public void RangePreservesReferencesAndOriginalOrder()
    {
        var late = new Sample { Timestamp = 1250 };
        var early = new Sample { Timestamp = 1000 };
        Sample[] samples = [new() { Timestamp = 9000 }, late, early];
        var index = new SampleTimeIndex();
        index.Update(samples, 0);
        Assert.That(index.Query(1000, 1250), Is.EqualTo(new[] { late, early }));
        Assert.That(samples[1], Is.SameAs(late));
        early.Timestamp = 9500;
        index.Update(samples, 1);
        Assert.That(index.Query(1000, 1250), Is.EqualTo(new[] { late }));
    }

    [TestCase(double.NaN, 1000)]
    [TestCase(2000, 1000)]
    [TestCase(0, double.PositiveInfinity)]
    public void InvalidRangeIsEmpty(double from, double to)
    {
        var index = new SampleTimeIndex();
        index.Update([new() { Timestamp = 1000 }], 0);
        Assert.That(index.Query(from, to), Is.Empty);
    }
}
