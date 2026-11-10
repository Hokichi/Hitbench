using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.CustomControls;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class ManageSamplesOpeningTests
{
    [TestCase(100)]
    [TestCase(1000)]
    public void OpeningCatalogRealizesOnlyVisibleRows(int count)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var files = new StubSampleFileService(Enumerable.Range(0, count).Select(i => $"sample{i:D4}.wav").ToArray());
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files),
            new StubSampleFileDialogService([]), new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler());
        var timer = Stopwatch.StartNew();
        UiTestPump.Run(vm.InitializeAsync(default));
        var initialization = timer.Elapsed;
        var drawer = new ManageSamplesDrawer(vm);
        var host = new Window { Content = drawer, Width = 720, Height = 480 };
        try
        {
            var construction = timer.Elapsed - initialization;
            using var view = new InMemoryView(host, scheduleLayout: false);
            var layout = timer.Elapsed - initialization - construction;
            var list = (ItemsControl)drawer.FindName("SampleRows");
            var realized = Descendants(drawer).OfType<OHsSampleRow>().Count();
            TestContext.Progress.WriteLine($"count={count}, initialize={initialization.TotalMilliseconds:F1}ms, construct={construction.TotalMilliseconds:F1}ms, layout={layout.TotalMilliseconds:F1}ms, realized={realized}");
            Assert.That(list.Items.Count, Is.EqualTo(count));
            Assert.That(realized, Is.GreaterThan(0).And.LessThan(count));
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(count - 1), Is.Null);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); host.Close(); }
    }

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void SectionsRecycleThroughTransitionsAndReload(FlowDirection direction)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var files = new StubSampleFileService([]);
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files),
            new StubSampleFileDialogService([]), new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler(), new StubSamplePreviewService());
        vm.AllowDefaults = true;
        var groups = new[] { vm.SkinDefaultEntries, vm.MapsetEntries, vm.Entries };
        foreach (var group in groups)
            for (var i = 0; i < 100; i++) group.Add(new($"sample{i:D4}.wav"));
        var drawer = new ManageSamplesDrawer(vm);
        var host = new Window { Content = drawer, Width = 720, Height = 480, FlowDirection = direction };
        try
        {
            using var view = new InMemoryView(host, scheduleLayout: false);
            var names = new[] { "SkinDefaultRows", "MapsetSampleRows", "SampleRows" };
            var sections = new[] { "SkinDefaultsExpander", "MapsetSamplesExpander", "ProjectSamplesExpander" };
            for (var i = 0; i < groups.Length; i++)
            {
                var list = (ItemsControl)drawer.FindName(names[i]);
                var section = (Expander)drawer.FindName(sections[i]);
                section.IsExpanded = false; view.UpdateLayout();
                section.IsExpanded = true; view.UpdateLayout();
                var scroll = Descendants(list).OfType<ScrollViewer>().Single();
                scroll.ScrollToEnd(); view.UpdateLayout();
                var container = list.ItemContainerGenerator.ContainerFromIndex(99);
                Assert.That(container, Is.Not.Null);
                var row = Descendants(container).OfType<OHsSampleRow>().Single();
                Assert.That(row.DataContext, Is.SameAs(groups[i][99]));
                Assert.That(row.PlayCommandParameter, Is.SameAs(groups[i][99]));
                scroll.ScrollToHome(); view.UpdateLayout();
                UiTestPump.Run(drawer.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
                view.UpdateLayout();
                Assert.That(list.ItemContainerGenerator.ContainerFromIndex(99), Is.Null);
            }
            UiTestPump.Run(vm.EditCommand.ExecuteAsync(vm.Entries[0])); vm.DraftStem = "draft";
            host.Height = 360; view.UpdateLayout();
            Assert.That(vm.DraftStem, Is.EqualTo("draft"));
            Assert.That(vm.ActiveEdit, Is.SameAs(vm.Entries[0]));
            vm.CancelEditCommand.Execute(null);
            vm.AllowDefaults = false; view.UpdateLayout();
            vm.AllowDefaults = true; view.UpdateLayout();
            drawer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            drawer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            vm.Entries.Clear(); view.UpdateLayout();
            for (var i = 0; i < 100; i++) vm.Entries.Add(new($"fresh{i}.wav"));
            view.UpdateLayout();
            Assert.That(((ItemsControl)drawer.FindName("SampleRows")).ItemContainerGenerator.ContainerFromIndex(99), Is.Null);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); host.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
