using osuHitsounder.Views.Pages;
namespace osuHitsounder.Test.Presentation;
public sealed class OHsWorkstationTimelineTests
{
    [TestCase(250d, 200d)]
    [TestCase(700d, 400d)]
    public void PageOffsetUsesExplicitInputs(double marker, double expected) =>
        Assert.That(OHsWorkstationTimeline.GetPlaybackPageOffset(marker, 0, 100, 500), Is.EqualTo(expected));
}
