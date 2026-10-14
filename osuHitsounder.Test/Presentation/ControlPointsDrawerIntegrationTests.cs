using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class ControlPointsDrawerIntegrationTests
{
    [Test]
    public void CollectionRecyclesAndCanReachLastPointWithoutShowingWindow()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var ctx=new ControlPointsContext("project.json",Guid.NewGuid(),Guid.NewGuid());
        var points=Enumerable.Range(0,500).Select(i=>new ControlPointSnapshot(Guid.NewGuid(),i*1000,100,"Normal")).ToArray();
        var vm=new ControlPointsDrawerVM(new(ctx,points,1,true,null,false,null),new WeakReferenceMessenger(),new ImmediateUiScheduler());
        try
        {
            var drawer=new OHsControlPointsDrawer(vm); UiTestPump.Layout(drawer,720,480);
            var rows=(ItemsControl)drawer.FindName("PointRows");
            Assert.That(Descendants(rows).OfType<NumericUpDown>().Count(),Is.LessThan(points.Length));
            Assert.That(drawer.HandleNavigationKey(Key.System,Key.End,ModifierKeys.Alt),Is.True);
            Assert.That(((FrameworkElement)rows.ItemContainerGenerator.ContainerFromIndex(499)).DataContext,Is.SameAs(vm.Rows[499]));
            Assert.That(vm.Rows[499].Timestamp,Is.EqualTo(499000));
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }
    [Test]
    public void ServiceTearsDownRecipientsOnPresentationFailureAndCancellation()
    {
        var ctx=new ControlPointsContext("project.json",Guid.NewGuid(),Guid.NewGuid()); var initial=new ControlPointsSnapshot(ctx,[],1,true,null,false,null);
        var messenger=new WeakReferenceMessenger(); ControlPointsDrawerVM? created=null;
        var service=new osuHitsounder.Services.Dialogs.ControlPointsDialogService(()=>null,System.Windows.Threading.Dispatcher.CurrentDispatcher,
            s=>created=new(s,messenger,new ImmediateUiScheduler())) { Present=(_,_)=>Task.FromException(new IOException("presentation failed")) };
        Assert.Throws<IOException>(()=>UiTestPump.Run(service.ShowAsync(initial,default)));
        Assert.That(created,Is.Not.Null); Assert.That(created!.AddCommand.CanExecute(null),Is.False);
        using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>UiTestPump.Run(service.ShowAsync(initial,cancellation.Token)));
    }
    [Test]
    public void ToolbarUsesClockActionAndOpensDrawerWithoutAddingPoint()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var project=new osuHitsounder.Core.Entities.ProjectData { Offsets=[0],BPMs=[120] }; var shown=0;
        var dialogs=new StubControlPointsDialogService { Show=(_,_)=>{shown++;return Task.CompletedTask;} };
        var vm=new osuHitsounder.ViewModels.Shell.WorkstationPageVM(new StubWorkstationSourceService {Load=(_,_)=>Task.FromResult(new osuHitsounder.Core.Entities.WorkstationSource("map","song",project))},
            new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler(),
            messenger:new WeakReferenceMessenger(),controlPointsDialog:dialogs);
        try
        {
            UiTestPump.Run(vm.LoadAsync("project.json",default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var page=new osuHitsounder.Views.Pages.WorkstationPage(vm); var button=(BalloonButton)page.FindName("AddControlPointButton");
            UiControlContract.AssertStyle<BalloonButton>(button,"OHsWorkstationControlPointsButtonStyle");
            Assert.That(button.ButtonIcon,Is.SameAs(page.FindResource("ClockPlusOutline")));
            UiTestPump.Run(vm.AddControlPointCommand.ExecuteAsync(null)); Assert.That(shown,Is.EqualTo(1)); Assert.That(project.ControlPoints,Is.Empty);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
    [Test]
    public void InputsCommandsCloseAndStyleContractsWorkWithoutShowingWindow()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var messenger=new WeakReferenceMessenger(); var ctx=new ControlPointsContext("project.json",Guid.NewGuid(),Guid.NewGuid()); var id=Guid.NewGuid();
        var vm=new ControlPointsDrawerVM(new(ctx,[new(id,1000,50,"Normal")],1,true,null,false,null),messenger,new ImmediateUiScheduler()); vm.Initialize();
        var requests=new List<SetControlPointRequestedMessage>(); var added=0;
        messenger.Register<SetControlPointRequestedMessage>(this,(_,m)=>requests.Add(m)); messenger.Register<AddControlPointRequestedMessage>(this,(_,_)=>added++);
        try
        {
            var drawer=new OHsControlPointsDrawer(vm); UiTestPump.Layout(drawer,720,480);
            var list=(ItemsControl)drawer.FindName("PointRows"); list.UpdateLayout();
            UiControlContract.AssertStyle<ItemsControl>(list,"OHsVirtualizedDrawerItemsStyle");
            Assert.That(VirtualizingPanel.GetIsVirtualizing(list),Is.True); Assert.That(VirtualizingPanel.GetVirtualizationMode(list),Is.EqualTo(VirtualizationMode.Recycling));
            var number=Descendants(list).OfType<NumericUpDown>().Single(); var combo=Descendants(list).OfType<ComboBox>().Single();
            UiControlContract.AssertStyle<NumericUpDown>(number,"OHsControlPointsVolumeStyle"); UiControlContract.AssertStyle<ComboBox>(combo,"OHsControlPointsSamplesetStyle");
            Assert.That(BindingOperations.GetBinding(number,NumericUpDown.ValueProperty)!.Mode,Is.EqualTo(BindingMode.TwoWay));
            Assert.That(BindingOperations.GetBinding(combo,ComboBox.SelectedItemProperty)!.Mode,Is.EqualTo(BindingMode.TwoWay));
            var editor=(TextBox)number.Template.FindName("PART_TextBox",number);
            editor.Text="25"; Assert.That(number.CommitInput(),Is.True); Assert.That(requests.Last().Volume,Is.EqualTo(25));
            Assert.That(BindingOperations.IsDataBound(number,NumericUpDown.ValueProperty),Is.True);
            combo.SetCurrentValue(ComboBox.SelectedItemProperty,"Drum"); Assert.That(requests.Last().Sampleset,Is.EqualTo("Drum"));
            editor.Text="25.5"; Assert.That(drawer.TryPrepareClose(),Is.False); Assert.That(requests.Last().Volume,Is.EqualTo(25));
            editor.Text="0"; Assert.That(drawer.TryPrepareClose(),Is.True); Assert.That(requests.Last().Volume,Is.Zero);
            var add=(BalloonButton)drawer.FindName("AddPointButton"); Assert.That(add.ButtonIcon,Is.SameAs(drawer.FindResource("PlusSolid"))); add.Command.Execute(null); Assert.That(added,Is.EqualTo(1));
            Assert.That(drawer.HandleNavigationKey(Key.PageDown,Key.None,ModifierKeys.None),Is.True);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); messenger.UnregisterAll(this); }
    }
}
