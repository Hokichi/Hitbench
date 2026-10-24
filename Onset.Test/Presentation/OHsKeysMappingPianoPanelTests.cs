using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Onset.Resources.DataModels;
using Onset.ViewModels.Popups;
using Onset.Views.Popups;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsKeysMappingPianoPanelTests
{
    private KeysMappingDrawerVM vm = null!;
    private OHsKeysMappingDrawer drawer = null!;
    private Window host = null!;
    private InMemoryView view = null!;
    private ListBox selector = null!;
    private OHsKeysMappingPianoPanel panel = null!;

    [SetUp]
    public void SetUp()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var context = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        vm = new(new(context, "Piano", 100, new Dictionary<string, string>(), [], [], 0),
            new WeakReferenceMessenger(), new ImmediateUiScheduler(), new StubSampleFileService([]), new StubSampleFileDialogService((string?)null));
        drawer = new(vm); host = new Window { Content = drawer, Width = 600, Height = 500 };
        view = new(host);
        drawer.FocusInitialAction(); view.UpdateLayout();
        selector = (ListBox)drawer.FindName("KeySelector");
        panel = Descendants(selector).OfType<OHsKeysMappingPianoPanel>().Single();
    }

    [TearDown]
    public void TearDown()
    {
        UiTestPump.Run(vm.DeactivateAsync()); view.Dispose(); host.Close();
    }

    [TestCase(20, 20, "C1")]
    [TestCase(100, 20, "C#1")]
    [TestCase(100, 160, "D1")]
    [TestCase(200, 20, "D#1")]
    [TestCase(300, 20, "F1")]
    [TestCase(400, 20, "F#1")]
    [TestCase(400, 160, "G1")]
    public void OverlappingKeysHitTheCorrectPitch(double x, double y, string key)
    {
        // Explicit input geometry, independent of the production style's sizing defaults.
        panel.NaturalKeyWidth = 100; panel.NaturalKeyHeight = 200;
        panel.SharpKeyWidth = 60; panel.SharpKeyHeight = 120;
        panel.SetHorizontalOffset(0); view.UpdateLayout();
        var hit = panel.InputHitTest(new Point(x, y)) as DependencyObject;
        while (hit is not null && hit is not ListBoxItem) hit = VisualTreeHelper.GetParent(hit);
        Assert.That(((hit as ListBoxItem)?.DataContext as KeyMappingEntry)?.Key, Is.EqualTo(key));
    }

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void EveryPitchCanBeRealizedAndSelectedAfterRecycling(FlowDirection flow)
    {
        host.FlowDirection = flow; view.UpdateLayout();
        for (var index = 0; index < 108; index++)
        {
            panel.BringIndexIntoViewPublic(index); view.UpdateLayout();
            var item = (ListBoxItem)selector.ItemContainerGenerator.ContainerFromIndex(index);
            Assert.That(item.DataContext, Is.SameAs(vm.Rows[index]));
            selector.SetCurrentValue(ListBox.SelectedItemProperty, item.DataContext);
            UiTestPump.Run(drawer.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.DataBind).Task);
            Assert.That(vm.SelectedEntry, Is.SameAs(vm.Rows[index]));
        }
        Assert.That(Descendants(selector).OfType<ListBoxItem>().Count(), Is.LessThan(108));
    }

    [Test]
    public void NativeContainerPinSurvivesScrollingWithoutChangingItsKey()
    {
        panel.BringIndexIntoViewPublic(0); view.UpdateLayout();
        var pinned = (ListBoxItem)selector.ItemContainerGenerator.ContainerFromIndex(0);
        VirtualizingPanel.SetIsContainerVirtualizable(pinned, false);
        panel.BringIndexIntoViewPublic(107); view.UpdateLayout();
        Assert.That(selector.ItemContainerGenerator.ContainerFromIndex(0), Is.SameAs(pinned));
        Assert.That(pinned.DataContext, Is.SameAs(vm.Rows[0]));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var child = 0; child < VisualTreeHelper.GetChildrenCount(root); child++)
            foreach (var value in Descendants(VisualTreeHelper.GetChild(root, child))) yield return value;
    }
}
