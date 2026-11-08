using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Helpers;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA),NonParallelizable]
public sealed class ManageMapsetSamplesIntegrationTests
{
    [Test]
    public void MapsetRowsUseSharedContractAndCommandsAndKeepVirtualization()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var names=Enumerable.Range(0,500).Select(i=>$"Mapset/drum-hitnormal{i}.wav").ToArray();
        var files=new StubSampleFileService(names);var vm=new ManageSamplesPopupVM(new("project.json",null,Guid.NewGuid()),files,new(files),
            new StubSampleFileDialogService([]),new StubSampleRemovalDialogService(),new WeakReferenceMessenger(),new ImmediateUiScheduler());
        var drawer=new ManageSamplesDrawer(vm);KeyboardNavigationPolicy.SetSuppressTabNavigation(drawer,true);
        var host=new Window { Content=drawer,Width=720,Height=600 };
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default));using var view=new InMemoryView(host,scheduleLayout:false);
            var expander=(Expander)drawer.FindName("MapsetSamplesExpander");UiControlContract.AssertStyle<Expander>(expander,typeof(Expander));
            var list=(ItemsControl)drawer.FindName("MapsetSampleRows");UiControlContract.AssertStyle<ItemsControl>(list,"OHsVirtualizedDrawerItemsStyle");
            Assert.That(list.Items.Count,Is.EqualTo(500));Assert.That(list.ItemContainerGenerator.ContainerFromIndex(499),Is.Null);
            var row=Children(list).OfType<OHsSampleRow>().First();UiControlContract.AssertStyle<OHsSampleRow>(row,"OHsManageSamplesDragSourceStyle");
            Assert.That(row.FileName,Is.EqualTo(((Hitbench.Resources.DataModels.SampleManagementEntry)row.DataContext).DisplayFileName));
            Assert.That(row.PlayCommand,Is.SameAs(vm.PlayCommand));Assert.That(row.PlayCommandParameter,Is.SameAs(row.DataContext));
            Assert.That(Children(row).OfType<Control>().All(c=>!c.IsTabStop),Is.True);
            drawer.FocusSampleRow(list,499);view.UpdateLayout();Assert.That(list.ItemContainerGenerator.ContainerFromIndex(499),Is.Not.Null);
            expander.IsExpanded=false;view.UpdateLayout();Assert.That(vm.MapsetEntries.Count,Is.EqualTo(500));
        }
        finally { UiTestPump.Run(vm.DeactivateAsync());host.Close(); }
    }
    [Test]
    public void MapsetDragUsesCanonicalPayloadAndClearsFeedbackWhenRemoved()
    {
        using var context=new ManageSamplesDragTestContext();
        var entry=new Hitbench.Resources.DataModels.SampleManagementEntry("Mapset/renamed.wav");context.Vm.MapsetEntries.Add(entry);
        UiTestPump.Layout(context.Host,800,600);var row=context.RowFor(entry);var calls=0;
        context.Drawer.SetSampleDragHandler((_,data,_)=>
        {
            calls++;Assert.That(row.IsDragSource,Is.True);
            var payload=(Hitbench.Resources.DataModels.OHsSampleTrackDragData)data.GetData(typeof(Hitbench.Resources.DataModels.OHsSampleTrackDragData));
            Assert.That(payload.FileName,Is.EqualTo("Mapset/renamed.wav"));
            context.Vm.MapsetEntries.Remove(entry);
            Assert.That(context.Drawer.GetSampleDragContinuation(DragAction.Continue),Is.EqualTo(DragAction.Cancel));
            return DragDropEffects.None;
        });
        Assert.That(context.Drawer.TryStartSampleDrag(entry,new Vector(100,100)),Is.True);Assert.That(calls,Is.EqualTo(1));Assert.That(row.IsDragSource,Is.False);
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {yield return parent;for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) foreach(var child in Children(VisualTreeHelper.GetChild(parent,i)))yield return child;}
}
