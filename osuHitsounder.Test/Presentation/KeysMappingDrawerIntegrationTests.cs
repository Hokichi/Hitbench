using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;
namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class KeysMappingDrawerIntegrationTests
{
    [Test]
    public void OuterOpenBindsFolderCommandAndSharedActionStyle()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var ctx = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var vm = new KeysMappingDrawerVM(new(ctx, "Piano", 100, new Dictionary<string, string>(), [], [], 0), new WeakReferenceMessenger(), new ImmediateUiScheduler(), new StubSampleFileService([]), new StubSampleFileDialogService((string?)null), new StubMappingConflictDialogService());
        var drawer = new OHsKeysMappingDrawer(vm);
        var host = new Window { Content = drawer, DataContext = vm, Width = 600, Height = 400 };
        using var view = new InMemoryView(host);
        try
        {
            UiTestPump.Run(drawer.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.DataBind).Task);
            var open = Descendants(drawer).OfType<osuHitsounder.Resources.CustomControls.BalloonButton>().Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Import key mappings from folder");
            open.GetBindingExpression(System.Windows.Controls.Primitives.ButtonBase.CommandProperty)!.UpdateTarget();
            Assert.That(open.Command, Is.SameAs(vm.OpenFolderCommand));
            Assert.That(open.Style, Is.SameAs(drawer.FindResource("OHsKeysActionStyle")));
            Assert.That(open.IsEnabled, Is.True);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); host.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child; }
    [Test]
    public void KeyboardNavigationCanReachLastVirtualizedKey()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var ctx = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var vm = new KeysMappingDrawerVM(new(ctx, "Piano", 100, new Dictionary<string, string>(), [], [], 0), new WeakReferenceMessenger(), new ImmediateUiScheduler(), new StubSampleFileService([]), new StubSampleFileDialogService((string?)null));
        var drawer = new OHsKeysMappingDrawer(vm);
        var host = new Window { Content = drawer, DataContext = vm, Width = 600, Height = 400 };
        using var view = new InMemoryView(host);
        try
        {
            view.UpdateLayout();
            var rows = (ItemsControl)drawer.FindName("KeyRows");
            UiTestPump.Run(drawer.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.DataBind).Task);
            rows.GetBindingExpression(ItemsControl.ItemsSourceProperty)!.UpdateTarget();
            view.UpdateLayout();
            Assert.That(rows.DataContext, Is.SameAs(vm));
            Assert.That(rows.ItemsSource, Is.SameAs(vm.Rows));
            Assert.That(drawer.HandleNavigationKey(Key.System, Key.End, ModifierKeys.Alt), Is.True);
            Assert.That(((FrameworkElement)rows.ItemContainerGenerator.ContainerFromIndex(107)).DataContext, Is.TypeOf<KeyMappingEntry>());
            Assert.That(((KeyMappingEntry)((FrameworkElement)rows.ItemContainerGenerator.ContainerFromIndex(107)).DataContext).Key, Is.EqualTo("B9"));
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); host.Close(); }
    }
}
