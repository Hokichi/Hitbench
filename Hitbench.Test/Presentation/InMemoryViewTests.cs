using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class InMemoryViewTests
{
    [Test]
    public void LayoutAndLifecycleNeverCreateNativeWindow()
    {
        var content = new Grid();
        var loads = 0;
        var unloads = 0;
        content.Loaded += (_, _) => loads++;
        content.Unloaded += (_, _) => unloads++;
        var window = new Window { Content = content, Width = 640, Height = 360 };
        try
        {
            using (var view = new InMemoryView(window))
            {
                view.UpdateLayout();
                Assert.Multiple(() =>
                {
                    Assert.That(content.RenderSize, Is.EqualTo(new Size(640, 360)));
                    Assert.That(loads, Is.EqualTo(1));
                    Assert.That(unloads, Is.Zero);
                    Assert.That(new WindowInteropHelper(window).Handle, Is.EqualTo(IntPtr.Zero));
                    Assert.That(window.IsVisible, Is.False);
                    Assert.That(PresentationSource.FromVisual(content), Is.TypeOf<InMemoryPresentationSource>());
                    Assert.That(PresentationSource.FromVisual(content)!.CompositionTarget, Is.Null);
                });
            }
            Assert.That(unloads, Is.EqualTo(1));
            Assert.That(PresentationSource.FromVisual(content), Is.Null);
        }
        finally { window.Close(); }
    }
}
