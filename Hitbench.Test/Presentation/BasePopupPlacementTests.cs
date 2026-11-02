using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Hitbench.Resources.CustomControls;
using Hitbench.Services.Ui;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class BasePopupPlacementTests
{
    [Test]
    public void CurrentOwnerBoundsDetermineCenterIndependentlyOfPreviousPosition()
    {
        var owner = new Rect(100, 200, 1200, 800);
        foreach (var oldPosition in new[] { new Point(0, 0), new Point(800, 900) })
            Assert.That(PopupPlacement.GetCenteredPosition(owner, oldPosition, new Size(500, 300), new Size(600, 400)), Is.EqualTo(new Point(400, 400)));
        // Maximizing changes the owner rectangle, even when its restored position is unchanged.
        Assert.That(PopupPlacement.GetCenteredPosition(new Rect(0, 0, 1920, 1080), new Point(800, 900),
            new Size(500, 300), new Size(600, 400)), Is.EqualTo(new Point(660, 340)));
    }

    [TestCase(1.5, 1.5, -960, 180)]
    [TestCase(1.5, 2, -960, 90)]
    public void ScreenBoundsUsePopupDeviceTransformIncludingMonitorOffset(double dpiX, double dpiY, double left, double top)
    {
        var bounds = PopupPlacement.ToWpfBounds(new Rect(-1920, 0, 1920, 1080), new Matrix(1 / dpiX, 0, 0, 1 / dpiY, 0, 0));
        Assert.That(bounds, Is.Not.Null);
        Assert.That(PopupPlacement.GetCenteredPosition(bounds, default, new Size(100, 100), new Size(640, 360)), Is.EqualTo(new Point(left, top)));
    }

    [Test]
    public void InvalidBoundsKeepPreviousCenter()
    {
        foreach (Rect? owner in new Rect?[] { null, Rect.Empty, new Rect(0, 0, 0, 100), new Rect(double.NaN, 0, 100, 100) })
            Assert.That(PopupPlacement.GetCenteredPosition(owner, new Point(200, 300), new Size(400, 200), new Size(600, 400)), Is.EqualTo(new Point(100, 200)));
        Assert.That(PopupPlacement.ToWpfBounds(Rect.Empty, Matrix.Identity), Is.Null);
        Assert.That(PopupPlacement.ToWpfBounds(new Rect(0, 0, 100, 100), new Matrix(0, 0, 0, 1, 0, 0)), Is.Null);
        Assert.That(PopupPlacement.ToWpfBounds(new Rect(0, 0, 100, 100), new Matrix(double.NaN, 0, 0, 1, 0, 0)), Is.Null);
    }

    [Test]
    public void MissingNativeSourceFallsBackWithoutCreatingWindowHandle()
    {
        var popup = new BasePopup();
        try
        {
            Assert.That(PopupPlacement.GetOwnerBounds(popup), Is.Null);
            Assert.That(new WindowInteropHelper(popup).Handle, Is.EqualTo(nint.Zero));
        }
        finally { popup.Close(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RepeatedOwnerCenteredResizesCommitTargets(bool animate)
    {
        var popup = new BasePopup { Left = 400, Top = 400 };
        try
        {
            var owner = new Rect(100, 200, 1200, 800);
            popup.RecenterForSizeChange(new Size(600, 400), new Size(800, 600), owner, animate);
            AssertTarget(popup, new Point(300, 300));
            popup.RecenterForSizeChange(new Size(800, 600), new Size(600, 400), owner, animate);
            AssertTarget(popup, new Point(400, 400));
            // Disabling motion must remove clocks even if the target has not changed.
            popup.RecenterForSizeChange(new Size(600, 400), new Size(600, 400), owner, false);
            Assert.That(new Point(popup.Left, popup.Top), Is.EqualTo(new Point(400, 400)));
            Assert.That(DependencyPropertyHelper.GetValueSource(popup, Window.LeftProperty).IsAnimated, Is.False);
            Assert.That(DependencyPropertyHelper.GetValueSource(popup, Window.TopProperty).IsAnimated, Is.False);
        }
        finally { popup.BeginAnimation(Window.LeftProperty, null); popup.BeginAnimation(Window.TopProperty, null); popup.Close(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InterruptedFallbackResizesPreserveCommittedCenter(bool animate)
    {
        var popup = new BasePopup { Left = 200, Top = 300 };
        try
        {
            popup.RecenterForSizeChange(new Size(400, 200), new Size(600, 400), null, animate);
            AssertTarget(popup, new Point(100, 200));
            popup.RecenterForSizeChange(new Size(600, 400), new Size(400, 200), null, animate);
            AssertTarget(popup, new Point(200, 300));
            popup.RecenterForSizeChange(new Size(400, 200), new Size(400, 200), null, false);
            Assert.That(new Point(popup.Left, popup.Top), Is.EqualTo(new Point(200, 300)));
        }
        finally { popup.BeginAnimation(Window.LeftProperty, null); popup.BeginAnimation(Window.TopProperty, null); popup.Close(); }
    }

    private static void AssertTarget(BasePopup popup, Point expected)
    {
        Assert.That(new Point((double)popup.GetAnimationBaseValue(Window.LeftProperty),
            (double)popup.GetAnimationBaseValue(Window.TopProperty)), Is.EqualTo(expected));
    }
}
