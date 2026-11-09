using Hitbench.Resources.Helpers;

namespace Hitbench.Test.Presentation;

public class OHsTimelineViewportTests
{
    [TestCase(0, -100)] [TestCase(5, 400)] [TestCase(10, 900)]
    public void StaticCoordinatesKeepPlaybackAtCenter(double seconds, double offset)
    {
        Assert.That(OHsTimelineViewport.GetHorizontalBounds(1000, 200, 100), Is.EqualTo((-100d, 900d)));
        Assert.That(OHsTimelineViewport.GetCenteredOffset(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(10), 1000, 200), Is.EqualTo(offset));
        Assert.That(OHsTimelineViewport.GetPositionAt(100, offset, 1000, TimeSpan.FromSeconds(10)), Is.EqualTo(TimeSpan.FromSeconds(seconds)));
    }

    [Test]
    public void DynamicBoundsAndDraggingPreserveDirectionAndClamp()
    {
        Assert.That(OHsTimelineViewport.GetHorizontalBounds(1000, 200, 0), Is.EqualTo((0d, 800d)));
        Assert.That(OHsTimelineViewport.GetDragOffset(400, 50, 80, -100, 900), Is.EqualTo(370));
        Assert.That(OHsTimelineViewport.GetDragOffset(400, 50, 20, -100, 900), Is.EqualTo(430));
        Assert.That(OHsTimelineViewport.GetDragOffset(400, 0, 1000, -100, 900), Is.EqualTo(-100));
        Assert.That(OHsTimelineViewport.GetDragOffset(400, 0, -1000, -100, 900), Is.EqualTo(900));
    }

    [TestCase(0)] [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)]
    public void InvalidWidthDoesNotProduceScrollOrSeek(double width)
    {
        Assert.That(OHsTimelineViewport.GetHorizontalBounds(width, 200, 100), Is.EqualTo((0d, 0d)));
        Assert.That(OHsTimelineViewport.GetCenteredOffset(TimeSpan.Zero, TimeSpan.FromSeconds(10), width, 200), Is.Zero);
        Assert.That(OHsTimelineViewport.GetPositionAt(100, 0, width, TimeSpan.FromSeconds(10)), Is.Null);
    }
}
