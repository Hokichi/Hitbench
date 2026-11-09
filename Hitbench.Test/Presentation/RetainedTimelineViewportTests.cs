using Hitbench.Resources.Helpers;

namespace Hitbench.Test.Presentation;

public class RetainedTimelineViewportTests
{
    [Test]
    public void FractionalOffsetsReuseBoundedWindowAndRebaseContinuously()
    {
        var viewport = new RetainedTimelineViewport();
        Assert.That(viewport.Update(400, 200, true), Is.True);
        Assert.That((viewport.Origin, viewport.Start, viewport.Width), Is.EqualTo((400d, -100d, 400d)));
        for (var offset = 400.25; offset <= 500; offset += .25)
        { Assert.That(viewport.Update(offset, 200, true), Is.False); Assert.That(viewport.Translation, Is.EqualTo(400 - offset)); }
        Assert.That(viewport.Update(500.25, 200, true), Is.True);
        Assert.That(650 - viewport.Origin + viewport.Translation, Is.EqualTo(650 - 500.25));
        Assert.That(viewport.Update(400.25, 200, true), Is.False);
        Assert.That(viewport.Update(400, 200, true), Is.True);
        viewport.Invalidate(); Assert.That(viewport.Update(400, 200, true), Is.True);
    }
    [Test]
    public void DynamicAndInvalidInputsStayFinite()
    {
        var viewport = new RetainedTimelineViewport();
        Assert.That(viewport.Update(10, 200, false), Is.True);
        Assert.That(viewport.Update(10, 200, false), Is.False);
        Assert.That(viewport.Update(10.25, 200, false), Is.True);
        Assert.That(viewport.Width, Is.EqualTo(200));
        viewport.Update(double.NaN, double.PositiveInfinity, true);
        Assert.That(double.IsFinite(viewport.Translation), Is.True);
        Assert.That(viewport.Width, Is.Zero);
    }
}
