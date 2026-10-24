using System.Windows;
using System.Windows.Controls;
using Hitbench.Resources.CustomControls;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class FadingScrollViewerContractTests
{
    [TestCase(false), TestCase(true)]
    public void ScrollViewerVariantsShareTemplateAndScroll(bool fading)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        ScrollViewer viewer = fading ? new FadingScrollViewer() : new ScrollViewer();
        viewer.Content = new Border { Width = 500, Height = 500 };
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        UiTestPump.Layout(viewer, 100, 100);
        UiControlContract.AssertTemplate(viewer, "DefaultScrollViewerTemplate");
        viewer.ScrollToVerticalOffset(40);
        UiTestPump.Layout(viewer, 100, 100);
        Assert.That(viewer.VerticalOffset, Is.EqualTo(40));
        var template = viewer.Template;
        viewer.Template = null;
        viewer.ApplyTemplate();
        viewer.Template = template;
        Assert.DoesNotThrow(() => UiTestPump.Layout(viewer, 100, 100));
    }
}
