using System.Windows;
using osuHitsounder.Services.Ui;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsWindowFrameTests
{
    [Test]
    public void SupportedBackdropSucceedsOrFallsBackAndDisposesOnce()
    {
        bool enabled = false;
        bool succeeds = true;
        int calls = 0, resets = 0;
        var frame = new OHsWindowFrame(new Window(), value => enabled = value,
            _ => { calls++; return succeeds; }, _ => resets++);
        frame.RefreshBackdrop(42, true, false);
        Assert.That(enabled, Is.True);
        frame.RefreshBackdrop(42, true, true);
        Assert.That(enabled, Is.False);
        succeeds = false;
        frame.RefreshBackdrop(42, true, false);
        Assert.That(enabled, Is.False);
        frame.RefreshBackdrop(42, false, false);
        Assert.That(calls, Is.EqualTo(2));
        frame.Dispose();
        var prior = resets;
        frame.Dispose();
        frame.RefreshBackdrop(42, true, false);
        Assert.That(resets, Is.EqualTo(prior));
        Assert.That(calls, Is.EqualTo(2));
    }

    [TestCase(4, 100, 10), TestCase(1276, 100, 11), TestCase(100, 4, 12)]
    [TestCase(4, 4, 13), TestCase(1276, 4, 14), TestCase(100, 716, 15)]
    [TestCase(4, 716, 16), TestCase(1276, 716, 17), TestCase(100, 100, 1)]
    public void ResizeHitTestsHonorEightDips(double x, double y, int expected)
    {
        var point = new Point(x, y);
        Assert.That(OHsWindowFrame.GetResizeHitTest(point, new Size(1280, 720), true, false), Is.EqualTo(expected));
        Assert.That(OHsWindowFrame.GetResizeHitTest(point, new Size(1280, 720), true, true), Is.EqualTo(1));
        Assert.That(OHsWindowFrame.GetResizeHitTest(point, new Size(1280, 720), false, false), Is.EqualTo(1));
        Assert.That(OHsWindowFrame.GetResizeHitTest(point, new Size(), true, false), Is.EqualTo(1));
    }

    [Test]
    public void MaximizeBoundsUseWorkAreaOnNegativeMonitor()
    {
        var info = OHsWindowFrame.GetMaximizeInfo(
            new NativeRect { Left = -1920, Right = 0, Bottom = 1080 },
            new NativeRect { Left = -1920, Right = 0, Bottom = 1040 }, new Size(641, 361), 1.25, 1.5);
        Assert.Multiple(() =>
        {
            Assert.That(info.MaxPosition.X, Is.Zero);
            Assert.That(info.MaxPosition.Y, Is.Zero);
            Assert.That(info.MaxSize.X, Is.EqualTo(1920));
            Assert.That(info.MaxSize.Y, Is.EqualTo(1040));
            Assert.That(info.MinTrackSize.X, Is.EqualTo(802));
            Assert.That(info.MinTrackSize.Y, Is.EqualTo(542));
        });
    }
}
