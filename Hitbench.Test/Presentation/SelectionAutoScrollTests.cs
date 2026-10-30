using Hitbench.Resources.Helpers;
namespace Hitbench.Test.Presentation;
public sealed class SelectionAutoScrollTests
{
    [TestCase(100, 20, 0)]
    [TestCase(110, 20, 3.2)]
    [TestCase(-10, 20, -3.2)]
    [TestCase(300, 1000, 50)]
    [TestCase(double.NaN, 20, 0)]
    public void ScrollUsesOutsideDistanceAndBoundedElapsed(double pointer, double milliseconds, double expected) =>
        Assert.That(SelectionAutoScroll.GetDelta(pointer, 0, 100, TimeSpan.FromMilliseconds(milliseconds)), Is.EqualTo(expected).Within(1e-9));
}
