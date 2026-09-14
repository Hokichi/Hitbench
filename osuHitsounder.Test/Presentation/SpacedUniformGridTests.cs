using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class SpacedUniformGridTests
{
    [Test]
    public void FixedCardMathUsesFourColumnsAndFullyVisibleRows()
    {
        var panel = new SpacedUniformGrid
        {
            Columns = 4,
            ItemWidth = 336,
            ItemHeight = 108,
            HorizontalGap = 12,
            VerticalGap = 12,
            CanHorizontallyScroll = true,
            CanVerticallyScroll = true
        };

        panel.Measure(new Size(640, 948));
        panel.Arrange(new Rect(0, 0, 640, 948));

        Assert.Multiple(() =>
        {
            Assert.That(panel.VisibleRowCount, Is.EqualTo(8));
            Assert.That(panel.ItemsPerViewport, Is.EqualTo(32));
            Assert.That(panel.RowStride, Is.EqualTo(120));
            Assert.That(panel.ExtentWidth, Is.EqualTo(1380));
            Assert.That(panel.ViewportHeight, Is.EqualTo(948));
        });

        panel.Measure(new Size(640, 947));
        panel.Arrange(new Rect(0, 0, 640, 947));
        Assert.That(panel.VisibleRowCount, Is.EqualTo(7));
    }

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void RealizationIsBoundedRecycledMirroredAndReachable(FlowDirection direction)
    {
        var items = CreateItemsControl(direction);
        var window = new Window
        {
            Content = items,
            Width = 700,
            Height = 500,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            var panel = Descendants<SpacedUniformGrid>(items).Single();
            var scroll = Descendants<ScrollViewer>(items).Single();
            var initialChildren = panel.Children.Cast<UIElement>().ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(panel.ExtentWidth, Is.EqualTo(1380));
                Assert.That(panel.ViewportWidth, Is.LessThan(panel.ExtentWidth));
                Assert.That(scroll.ScrollableWidth, Is.GreaterThan(0));
                Assert.That(initialChildren.Length, Is.InRange(1, (panel.VisibleRowCount + 2) * 4));
            });

            var first = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(0)!;
            var fourth = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(3)!;
            var firstX = first.TranslatePoint(new Point(), panel).X;
            var fourthX = fourth.TranslatePoint(new Point(), panel).X;
            if (direction == FlowDirection.LeftToRight)
                Assert.That(firstX, Is.LessThan(fourthX));
            else
                Assert.That(firstX, Is.GreaterThan(fourthX));

            scroll.ScrollToVerticalOffset(panel.RowStride * 20);
            window.UpdateLayout();
            var scrolledChildren = panel.Children.Cast<UIElement>().ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(panel.VerticalOffset, Is.GreaterThan(0));
                Assert.That(items.ItemContainerGenerator.ContainerFromIndex(0), Is.Null);
                Assert.That(scrolledChildren.Length, Is.InRange(1, (panel.VisibleRowCount + 2) * 4));
                Assert.That(scrolledChildren.Intersect(initialChildren), Is.Not.Empty,
                    "Recycling should reuse at least one realized container.");
            });

            var last = (FrameworkElement)scrolledChildren[^1];
            panel.MakeVisible(last, new Rect(last.RenderSize));
            window.UpdateLayout();
            var top = last.TranslatePoint(new Point(), panel).Y;
            Assert.That(top, Is.GreaterThanOrEqualTo(-0.01));
            Assert.That(top + last.ActualHeight, Is.LessThanOrEqualTo(panel.ViewportHeight + 0.01));
        }
        finally
        {
            window.Close();
        }
    }

    private static ItemsControl CreateItemsControl(FlowDirection direction)
    {
        var panelFactory = new FrameworkElementFactory(typeof(SpacedUniformGrid));
        panelFactory.SetValue(SpacedUniformGrid.ColumnsProperty, 4);
        panelFactory.SetValue(SpacedUniformGrid.ItemWidthProperty, 336d);
        panelFactory.SetValue(SpacedUniformGrid.ItemHeightProperty, 108d);
        panelFactory.SetValue(SpacedUniformGrid.HorizontalGapProperty, 12d);
        panelFactory.SetValue(SpacedUniformGrid.VerticalGapProperty, 12d);

        var scrollFactory = new FrameworkElementFactory(typeof(ScrollViewer));
        scrollFactory.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scrollFactory.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scrollFactory.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scrollFactory.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));

        var items = new ItemsControl
        {
            FlowDirection = direction,
            ItemsSource = Enumerable.Range(0, 2000),
            ItemsPanel = new ItemsPanelTemplate(panelFactory),
            Template = new ControlTemplate(typeof(ItemsControl)) { VisualTree = scrollFactory }
        };
        VirtualizingPanel.SetIsVirtualizing(items, true);
        VirtualizingPanel.SetVirtualizationMode(items, VirtualizationMode.Recycling);
        return items;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
