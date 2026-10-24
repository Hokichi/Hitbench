using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hitbench.Resources.CustomControls;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class SpacedUniformGridTests
{
    [TestCase(2000, FlowDirection.LeftToRight), TestCase(20000, FlowDirection.RightToLeft)]
    public void OffsetsInsideRealizedRangeDoNotMeasureOrRegenerate(int count, FlowDirection direction)
    {
        var items = CreateItemsControl(direction);
        items.ItemsSource = Enumerable.Range(0, count);
        var window = new Window { Content = items, Width = 700, Height = 500 };
        using var view = new InMemoryView(window, scheduleLayout: false);
        var panel = Descendants<SpacedUniformGrid>(items).Single();
        panel.SetVerticalOffset(360.25);
        view.UpdateLayout();
        var measures = panel.MeasurePassCount;
        var realized = panel.RealizationPassCount;
        var prepared = panel.PreparedContainerCount;
        var first = (FrameworkElement)panel.Children[0];
        var y = first.TranslatePoint(new Point(), panel).Y;
        panel.SetVerticalOffset(360.75);
        view.UpdateLayout();
        Assert.Multiple(() =>
        {
            Assert.That(panel.MeasurePassCount, Is.EqualTo(measures));
            Assert.That(panel.RealizationPassCount, Is.EqualTo(realized));
            Assert.That(panel.PreparedContainerCount, Is.EqualTo(prepared));
            Assert.That(first.TranslatePoint(new Point(), panel).Y, Is.EqualTo(y - .5).Within(.01));
        });
        panel.InvalidateMeasure();
        view.UpdateLayout();
        Assert.That(panel.RealizationPassCount, Is.EqualTo(realized), "Child measurement must not restart unchanged generator work.");
        var version = panel.VerticalScrollRequestVersion;
        panel.BeginScrollRequest();
        panel.SetAnimatedVerticalOffset(360.25);
        panel.SetAnimatedVerticalOffset(360.75);
        Assert.That(panel.VerticalScrollRequestVersion, Is.EqualTo(version + 1));
        panel.SetVerticalOffset(480);
        view.UpdateLayout();
        Assert.That(panel.MeasurePassCount, Is.GreaterThan(measures));
        Assert.That(panel.PreparedContainerCount - prepared, Is.LessThanOrEqualTo(8));
        Assert.That(panel.Children.Count, Is.LessThanOrEqualTo((panel.VisibleRowCount + 3) * 4));
        items.ItemsSource = Enumerable.Range(0, 4);
        view.UpdateLayout();
        Assert.That(panel.VerticalOffset, Is.Zero);
        Assert.That(panel.Children.Count, Is.EqualTo(4));
        items.ItemsSource = Array.Empty<int>();
        view.UpdateLayout();
        Assert.That(panel.Children.Count, Is.Zero);
        Assert.That(panel.ExtentHeight, Is.Zero);
        window.Close();
    }
    [Test]
    public void CollectionRemovalAndGeometryChangesKeepViewportClamped()
    {
        var source = new System.Collections.ObjectModel.ObservableCollection<int>(Enumerable.Range(0, 80));
        var items = CreateItemsControl(FlowDirection.LeftToRight);
        items.ItemsSource = source;
        var window = new Window { Content = items, Width = 700, Height = 500 };
        using var view = new InMemoryView(window, scheduleLayout: false);
        var panel = Descendants<SpacedUniformGrid>(items).Single();
        panel.SetVerticalOffset(double.MaxValue);
        view.UpdateLayout();
        source.RemoveAt(79);
        source.RemoveAt(0);
        view.UpdateLayout();
        panel.Columns = 2;
        panel.ItemHeight = 200;
        window.Height = 800;
        view.UpdateLayout();
        Assert.That(panel.VerticalOffset, Is.InRange(0, panel.ExtentHeight - panel.ViewportHeight));
        Assert.That(panel.Children.Cast<FrameworkElement>().Select(child => child.DataContext), Does.Not.Contain(79));
        window.Close();
    }

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
        using var view = new InMemoryView(window);
        try
        {
            view.UpdateLayout();
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
            view.UpdateLayout();
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
            view.UpdateLayout();
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
