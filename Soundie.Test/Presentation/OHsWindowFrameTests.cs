using System.Windows;
using System.Runtime.InteropServices;
using Soundie.Services.Ui;

namespace Soundie.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsWindowFrameTests
{
    [TestCase(0), TestCase(1)]
    public void BothClientCalculationFormsPreserveFullWindowBounds(int form)
    {
        int activationCalls = 0;
        using var frame = new OHsWindowFrame(new Window(), _ => { }, _ => true, _ => { },
            defaultWindowProc: (_, _, _, _) => { activationCalls++; return 1; });
        var rectangles = new[]
        {
            new NativeRect { Right = 1280, Bottom = 720 },
            new NativeRect { Left = -1920, Right = 0, Bottom = 1040 },
            new NativeRect { Left = -32000, Top = -32000, Right = -31840, Bottom = -31972 },
            new NativeRect { Right = 1280, Bottom = 720 }
        };
        foreach (var bounds in rectangles)
        {
            var length = form == 0 ? Marshal.SizeOf<NativeRect>() : 3 * Marshal.SizeOf<NativeRect>() + IntPtr.Size;
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                bool activated = false, painted = false;
                var activationResult = frame.WindowProc(42, 0x0086, activationCalls % 2, 123, ref activated);
                var paintResult = frame.WindowProc(42, 0x0085, 1, 0, ref painted);
                Assert.Multiple(() =>
                {
                    Assert.That(activated, Is.True);
                    Assert.That(activationResult, Is.EqualTo((nint)1));
                    Assert.That(painted, Is.True);
                    Assert.That(paintResult, Is.EqualTo((nint)0));
                });
                Marshal.Copy(new byte[length], 0, buffer, length);
                Marshal.StructureToPtr(bounds, buffer, false);
                var before = new byte[length];
                Marshal.Copy(buffer, before, 0, length);
                bool handled = false;
                var result = frame.WindowProc(0, 0x0083, form, buffer, ref handled);
                var after = new byte[length];
                Marshal.Copy(buffer, after, 0, length);
                Assert.Multiple(() =>
                {
                    Assert.That(handled, Is.True);
                    Assert.That(result, Is.EqualTo((nint)0));
                    Assert.That(after, Is.EqualTo(before));
                });
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        Assert.That(activationCalls, Is.EqualTo(rectangles.Length));
    }

    [TestCase(0, 0), TestCase(0, 1), TestCase(1, 0), TestCase(1, 1)]
    public void NativeActivationDelegatesWithoutFrameRepaint(int activation, int nativeResult)
    {
        var calls = new List<(nint Handle, int Message, nint Activation, nint Region)>();
        using var frame = new OHsWindowFrame(new Window(), _ => { }, _ => true, _ => { },
            (_, _, _) => 0, (hwnd, message, wParam, lParam) =>
            {
                calls.Add((hwnd, message, wParam, lParam));
                return nativeResult;
            });
        frame.RefreshBackdrop(84, true, false);
        bool handled = false;
        var result = frame.WindowProc(42, 0x0086, activation, 123, ref handled);
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(new[] { ((nint)42, 0x0086, (nint)activation, (nint)(-1)) }));
            Assert.That(result, Is.EqualTo((nint)nativeResult));
            Assert.That(handled, Is.True);
        });
    }

    [TestCase(0), TestCase(1), TestCase(123)]
    public void NonClientPaintIsConsumedWithoutNativeDrawing(int region)
    {
        int calls = 0;
        using var frame = new OHsWindowFrame(new Window(), _ => { }, _ => true, _ => { },
            defaultWindowProc: (_, _, _, _) => { calls++; return 1; });
        bool handled = false;
        var result = frame.WindowProc(42, 0x0085, region, 0, ref handled);
        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(result, Is.EqualTo((nint)0));
            Assert.That(calls, Is.Zero);
        });
    }

    [Test]
    public void UnrelatedAndDisposedMessagesDoNotCallDefaultProcedure()
    {
        int calls = 0;
        using var frame = new OHsWindowFrame(new Window(), _ => { }, _ => true, _ => { },
            defaultWindowProc: (_, _, _, _) => { calls++; return 1; });
        bool handled = false;
        Assert.That(frame.WindowProc(42, 0x0400, 0, 0, ref handled), Is.EqualTo((nint)0));
        Assert.That(handled, Is.False);
        Assert.That(calls, Is.Zero);
        frame.Dispose();
        foreach (var message in new[] { 0x0086, 0x0085, 0x0083 })
        {
            handled = false;
            Assert.That(frame.WindowProc(42, message, 1, 123, ref handled), Is.EqualTo((nint)0));
            Assert.That(handled, Is.False);
            Assert.That(calls, Is.Zero);
        }
    }

    [Test]
    public void SupportedBackdropSucceedsOrFallsBackAndDisposesOnce()
    {
        bool enabled = false;
        bool succeeds = true;
        int calls = 0, resets = 0;
        var frame = new OHsWindowFrame(new Window(), value => enabled = value,
            _ => { calls++; return succeeds; }, _ => resets++, (_, _, _) => 0);
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

    [Test]
    public void BorderSuppressionSurvivesRefreshAndFallback()
    {
        var calls = new List<(nint Handle, int Attribute, int Value)>();
        var order = new List<string>();
        bool enabled = false, succeeds = true;
        using var frame = new OHsWindowFrame(new Window(), value => enabled = value,
            _ => succeeds, _ => order.Add("disable"), (handle, attribute, value) =>
            {
                calls.Add((handle, attribute, value));
                order.Add("border");
                return 0;
            });
        frame.RefreshBackdrop(42, true, false);
        Assert.That(enabled, Is.True);
        frame.RefreshBackdrop(42, true, false);
        frame.RefreshBackdrop(42, true, true);
        Assert.That(enabled, Is.False);
        succeeds = false;
        frame.RefreshBackdrop(42, true, false);
        frame.RefreshBackdrop(42, false, false, borderSupported: true);
        Assert.That(enabled, Is.False);
        Assert.That(calls, Is.EqualTo(Enumerable.Repeat(((nint)42, 34, unchecked((int)0xFFFFFFFE)), 5)));
        Assert.That(order, Is.EqualTo(new[] { "border", "border", "disable", "border", "disable", "border", "disable", "border" }));
    }

    [Test]
    public void UnsupportedBorderAndDisposedRefreshDoNotCallNative()
    {
        int calls = 0;
        using var frame = new OHsWindowFrame(new Window(), _ => { }, _ => true, _ => { },
            (_, _, _) => { calls++; return 0; });
        frame.RefreshBackdrop(0, true, false);
        frame.RefreshBackdrop(42, true, false, borderSupported: false);
        frame.Dispose();
        frame.RefreshBackdrop(42, true, false);
        Assert.That(calls, Is.Zero);
    }

    [TestCase(false), TestCase(true)]
    public void BorderAttributeFailureDoesNotDisableAcrylic(bool throws)
    {
        bool enabled = false;
        int calls = 0, resets = 0;
        using var frame = new OHsWindowFrame(new Window(), value => enabled = value, _ => true,
            _ => resets++, (_, _, _) =>
            {
                calls++;
                if (throws) throw new ExternalException("Unsupported border attribute");
                return unchecked((int)0x80070057);
            });
        Assert.DoesNotThrow(() => frame.RefreshBackdrop(42, true, false));
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(enabled, Is.True);
        Assert.That(resets, Is.Zero);
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
