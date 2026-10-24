using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Hitbench.Resources.CustomControls;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OuterGlowAdornerTests
{
    [SetUp]
    public void SetUp() => new SongsResponsiveLayoutTests().SetUp();

    [TestCase(120d, 80d, 7d, FlowDirection.LeftToRight)]
    [TestCase(240d, 100d, 13d, FlowDirection.RightToLeft)]
    public void HaloExcludesContentAndRemainsViewportBounded(double width, double height, double radius, FlowDirection direction)
    {
        var source = new Border { Width = 80, Height = height + 60, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var content = new Grid { Height = 500 };
        content.Children.Add(source);
        var viewer = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        var host = new AdornerDecorator { Child = viewer };
        using var view = new InMemoryView(new Window { Content = host, Width = width, Height = height, FlowDirection = direction });
        var viewport = (ScrollContentPresenter)viewer.Template.FindName("PART_ScrollContentPresenter", viewer);
        var effect = new DropShadowEffect { BlurRadius = radius, ShadowDepth = 0 };
        var adorner = new OuterGlowAdorner(source, viewport, effect);
        host.AdornerLayer.Add(adorner);
        try
        {
            Assert.That(adorner.UpdateViewport(0), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(adorner.Clip.FillContains(new Point(40, height / 2)), Is.False, "Never redraw card content.");
                Assert.That(adorner.Clip.FillContains(new Point(-radius / 2, height / 2)), Is.True);
                Assert.That(adorner.Clip.FillContains(new Point(80 + radius / 2, height / 2)), Is.True);
                Assert.That(adorner.Clip.FillContains(new Point(-radius - 1, height / 2)), Is.False);
                Assert.That(adorner.Clip.FillContains(new Point(-radius / 2, height + radius + 1)), Is.False, "Do not draw beyond the viewport halo gutter.");
            });
            var unchanged = adorner.Clip;
            adorner.UpdateViewport(0);
            Assert.That(adorner.Clip, Is.SameAs(unchanged), "Unchanged layout must not queue another layout pass.");
            effect.BlurRadius = radius + 6;
            Assert.That(adorner.Clip.FillContains(new Point(-radius - 2, height / 2)), Is.True);
            viewer.ScrollToVerticalOffset(height + 80);
            view.UpdateLayout();
            Assert.That(adorner.UpdateViewport(0), Is.False);
        }
        finally { host.AdornerLayer.Remove(adorner); adorner.Release(); }
    }

    [Test]
    public void FocusClearanceExcludesReservedRing()
    {
        var source = new Border { Width = 80, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var viewer = new ScrollViewer { Content = source };
        var host = new AdornerDecorator { Child = viewer };
        using var view = new InMemoryView(new Window { Content = host, Width = 120, Height = 80 });
        var viewport = (ScrollContentPresenter)viewer.Template.FindName("PART_ScrollContentPresenter", viewer);
        var adorner = new OuterGlowAdorner(source, viewport, new DropShadowEffect { BlurRadius = 13, ShadowDepth = 0 });
        host.AdornerLayer.Add(adorner);
        try
        {
            Assert.That(adorner.UpdateViewport(5), Is.True);
            Assert.That(adorner.Clip.FillContains(new Point(-3, 20)), Is.False);
            Assert.That(adorner.Clip.FillContains(new Point(-8, 20)), Is.True);
        }
        finally { host.AdornerLayer.Remove(adorner); adorner.Release(); }
    }

    [Test]
    public void OffscreenAndDisconnectedSourcesAreInactive()
    {
        var source = new Border();
        var adorner = new OuterGlowAdorner(source, new ScrollContentPresenter(), new DropShadowEffect());
        Assert.That(adorner.UpdateViewport(0), Is.False);
        adorner.Release();
        Assert.That(adorner.UpdateViewport(0), Is.False);
    }

    [Test]
    public void DecorationCannotReceiveInputOrFocus()
    {
        var context = new object();
        var source = new Border { DataContext = context };
        var adorner = new OuterGlowAdorner(source, new ScrollContentPresenter(), new DropShadowEffect());
        Assert.Multiple(() =>
        {
            Assert.That(adorner.AdornedElement, Is.SameAs(source));
            Assert.That(source.DataContext, Is.SameAs(context));
            Assert.That(adorner.IsHitTestVisible, Is.False);
            Assert.That(adorner.Focusable, Is.False);
        });
        adorner.Release();
    }
}
