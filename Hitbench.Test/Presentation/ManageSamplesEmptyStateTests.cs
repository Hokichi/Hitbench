using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class ManageSamplesEmptyStateTests
{
    [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5), TestCase(6), TestCase(7)]
    public void LabelsFollowTheirOwnCollection(int mask)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var files = new StubSampleFileService([]);
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files),
            new StubSampleFileDialogService([]), new StubSampleRemovalDialogService(),
            new WeakReferenceMessenger(), new ImmediateUiScheduler());
        vm.AllowDefaults = true;
        var drawer = new ManageSamplesDrawer(vm);
        var host = new Window { Content = drawer, Width = 720, Height = 600 };
        try
        {
            using var view = new InMemoryView(host, scheduleLayout: false);
            var groups = new[] { vm.SkinDefaultEntries, vm.MapsetEntries, vm.Entries };
            var labels = new[] { "SkinDefaultsExpander", "MapsetSamplesExpander", "ProjectSamplesExpander" }
                .Select(name => Descendants((Expander)drawer.FindName(name)).OfType<TextBlock>()
                    .Single(text => text.Text == "No samples found")).ToArray();
            void Check()
            {
                view.UpdateLayout();
                UiTestPump.Run(drawer.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
                for (var i = 0; i < groups.Length; i++)
                    Assert.That(labels[i].Visibility, Is.EqualTo(groups[i].Count == 0 ? Visibility.Visible : Visibility.Collapsed), $"Group {i}, mask {mask}");
            }
            for (var i = 0; i < groups.Length; i++)
                if ((mask & (1 << i)) != 0) groups[i].Add(new SampleManagementEntry("hit.wav"));
            Check();
            foreach (var group in groups)
            {
                group.Clear(); Check();
                group.Add(new SampleManagementEntry("hit.wav")); Check();
                group.RemoveAt(0); Check();
                group.Add(new SampleManagementEntry("hit.wav")); Check();
                group.Clear(); Check();
            }
            drawer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            drawer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            vm.Entries.Add(new SampleManagementEntry("reload.wav")); Check();
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); host.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
